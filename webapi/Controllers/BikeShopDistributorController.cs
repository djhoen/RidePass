using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Audit;
using Services.Distributors;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// A shop's distributor connections: connect an account, test it, sync now, disconnect.
    /// The unattended nightly sweep lives in TaskRunner; this is the screen around it.
    ///
    /// SettingsManage rather than CatalogManage throughout: these are account credentials, not
    /// catalog rows, and handing a distributor login to whoever can edit products is a wider blast
    /// radius than it needs to be.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
    public class BikeShopDistributorController : ControllerBase
    {
        private readonly IDistributorCredentialRepository _credentials;
        private readonly IDistributorSyncService _sync;
        private readonly IEnumerable<IDistributorCatalogSource> _sources;
        private readonly ITenantContext _tenantContext;
        private readonly IAuditLogger _audit;

        public BikeShopDistributorController(
            IDistributorCredentialRepository credentials,
            IDistributorSyncService sync,
            IEnumerable<IDistributorCatalogSource> sources,
            ITenantContext tenantContext,
            IAuditLogger audit)
        {
            _credentials = credentials;
            _sync = sync;
            _sources = sources;
            _tenantContext = tenantContext;
            _audit = audit;
        }

        private Guid TenantId => _tenantContext.TenantId;

        private IDistributorCatalogSource? SourceFor(string slug) =>
            _sources.FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Which distributors exist and what this shop has connected. The response carries no
        /// secret and no ciphertext, only whether a key is on file.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var statuses = await _credentials.ListStatuses(TenantId);
            var available = _sources.Select(s =>
            {
                var status = statuses.FirstOrDefault(x =>
                    string.Equals(x.Distributor, s.Slug, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    slug = s.Slug,
                    displayName = s.DisplayName,
                    // Whether this deployment can actually talk to them. False means "connect it
                    // and nothing will happen yet", which the UI needs to say honestly up front.
                    isAvailable = s.IsConfigured,
                    connected = status is not null,
                    status?.AccountNumber,
                    status?.Username,
                    isEnabled = status?.IsEnabled ?? false,
                    hasApiKey = status?.HasApiKey ?? false,
                    hasPassword = status?.HasPassword ?? false,
                    lastSyncAtUtc = status?.LastSyncAt is null
                        ? (DateTime?)null : DateTime.SpecifyKind(status.LastSyncAt.Value, DateTimeKind.Utc),
                    status?.LastStatus,
                    status?.LastError,
                    lastProductsSeen = status?.LastProductsSeen ?? 0,
                    lastVariantsUpdated = status?.LastVariantsUpdated ?? 0,
                };
            });
            return new ApiResponses().OkResult(available);
        }

        [HttpPut]
        public async Task<IActionResult> Connect([FromBody] ConnectDistributorRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var source = SourceFor(request.Distributor);
            if (source is null) return new ApiResponses().BadRequestResult("That distributor isn't supported.");

            // Null means "keep what's stored" all the way down to the repository's COALESCE, so an
            // admin fixing an account number doesn't have to re-enter a key we never showed them.
            var password = string.IsNullOrWhiteSpace(request.Password)
                ? null : EncryptionHelper.Encrypt(request.Password.Trim(), null);
            var apiKey = string.IsNullOrWhiteSpace(request.ApiKey)
                ? null : EncryptionHelper.Encrypt(request.ApiKey.Trim(), null);

            await _credentials.Upsert(TenantId, source.Slug,
                request.AccountNumber?.Trim(), request.Username?.Trim(),
                password, apiKey, request.IsEnabled);

            // No secret in the summary, only that one was set.
            await _audit.Log("distributor.connect",
                $"Connected {source.DisplayName} (account {request.AccountNumber ?? "n/a"})",
                "distributor", null, TenantId);

            return await List();
        }

        [HttpDelete("{distributor}")]
        public async Task<IActionResult> Disconnect(string distributor)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var source = SourceFor(distributor);
            if (source is null) return new ApiResponses().BadRequestResult("That distributor isn't supported.");

            await _credentials.Delete(TenantId, source.Slug);
            await _audit.Log("distributor.disconnect", $"Disconnected {source.DisplayName}",
                "distributor", null, TenantId);
            return await List();
        }

        /// <summary>Credential check only. Does not pull the catalog.</summary>
        [HttpPost("{distributor}/Test")]
        public async Task<IActionResult> Test(string distributor, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var source = SourceFor(distributor);
            if (source is null) return new ApiResponses().BadRequestResult("That distributor isn't supported.");

            var credential = await _credentials.Get(TenantId, source.Slug);
            if (credential is null) return new ApiResponses().BadRequestResult("Connect the account first.");

            var (ok, error) = await source.TestConnection(new DistributorCredentials(
                credential.AccountNumber,
                credential.Username,
                string.IsNullOrEmpty(credential.PasswordEncrypted)
                    ? null : EncryptionHelper.Decrypt(credential.PasswordEncrypted),
                string.IsNullOrEmpty(credential.ApiKeyEncrypted)
                    ? null : EncryptionHelper.Decrypt(credential.ApiKeyEncrypted)), ct);

            return new ApiResponses().OkResult(new { ok, error });
        }

        /// <summary>
        /// Pull now instead of waiting for tonight. Same code path as the sweep, so a shop that
        /// just connected can confirm it works rather than waiting a day to find out it doesn't.
        /// </summary>
        [HttpPost("{distributor}/Sync")]
        public async Task<IActionResult> SyncNow(string distributor, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var source = SourceFor(distributor);
            if (source is null) return new ApiResponses().BadRequestResult("That distributor isn't supported.");

            var result = await _sync.SyncTenant(TenantId, source.Slug, ct);
            if (!result.Ok) return new ApiResponses().BadRequestResult(result.Error ?? "The sync failed.");

            await _audit.Log("distributor.sync",
                $"Synced {source.DisplayName}: {result.ProductsSeen} products, "
                + $"{result.VariantsCreated} created, {result.VariantsUpdated} updated",
                "distributor", null, TenantId);

            return new ApiResponses().OkResult(new
            {
                result.ProductsSeen, result.VariantsCreated, result.VariantsUpdated,
            });
        }
    }
}
