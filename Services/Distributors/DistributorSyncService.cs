using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;

namespace Services.Distributors
{
    public class DistributorSyncSummary
    {
        public int TenantsConsidered { get; set; }
        public int Synced { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
    }

    public class DistributorSyncResult
    {
        public bool Ok { get; set; }
        public string? Error { get; set; }
        public int ProductsSeen { get; set; }
        public int VariantsCreated { get; set; }
        public int VariantsUpdated { get; set; }
        /// <summary>Nothing was attempted because this deployment can't talk to the distributor yet.
        /// Distinct from a failure: the shop did nothing wrong and can do nothing about it.</summary>
        public bool Skipped { get; set; }
    }

    public interface IDistributorSyncService
    {
        /// <summary>Sync one tenant's connection now. Used by the settings screen's "Sync now".</summary>
        Task<DistributorSyncResult> SyncTenant(Guid tenantId, string distributor, CancellationToken ct = default);

        /// <summary>The nightly sweep: every enabled connection that hasn't run in a day.</summary>
        Task<DistributorSyncSummary> SyncDueTenantsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Pulls each connected shop's distributor catalog into their own catalog, automatically.
    ///
    /// It deliberately owns very little. Fetching is the source's job, matching and writing is
    /// ImportCatalog's job (already tested: match by GTIN then MPN then SKU, write only the columns
    /// the feed carried, never touch stock), and this class is the part in between: decrypt, call,
    /// map, stamp provenance, record the outcome.
    ///
    /// THE RULE IT ENFORCES: every manufacturer name written here is stamped with the DISTRIBUTOR's
    /// source slug, never 'shop'. That is what stops per-dealer licensed content reaching the
    /// cross-tenant parts library, and it is set from the source rather than passed in so a caller
    /// cannot get it wrong.
    /// </summary>
    public class DistributorSyncService : IDistributorSyncService
    {
        private readonly IDistributorCredentialRepository _credentials;
        private readonly ICatalogImporter _catalog;
        private readonly IEnumerable<IDistributorCatalogSource> _sources;
        private readonly ILogger<DistributorSyncService> _logger;

        /// <summary>Daily. A distributor catalog changes on the order of days, and each run is a
        /// full pull, so anything faster is load without benefit.</summary>
        private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(24);

        public DistributorSyncService(
            IDistributorCredentialRepository credentials,
            ICatalogImporter catalog,
            IEnumerable<IDistributorCatalogSource> sources,
            ILogger<DistributorSyncService> logger)
        {
            _credentials = credentials;
            _catalog = catalog;
            _sources = sources;
            _logger = logger;
        }

        public IDistributorCatalogSource? SourceFor(string distributor) =>
            _sources.FirstOrDefault(s => string.Equals(s.Slug, distributor, StringComparison.OrdinalIgnoreCase));

        public async Task<DistributorSyncResult> SyncTenant(Guid tenantId, string distributor,
            CancellationToken ct = default)
        {
            var credential = await _credentials.Get(tenantId, distributor);
            if (credential is null)
            {
                return new DistributorSyncResult { Ok = false, Error = "That distributor isn't connected." };
            }
            return await Run(credential, ct);
        }

        public async Task<DistributorSyncSummary> SyncDueTenantsAsync(CancellationToken ct = default)
        {
            var summary = new DistributorSyncSummary();

            // Nothing can run until a source has a real transport, so don't even query. This is why
            // a deployment with no wired distributor is silent rather than noisy.
            if (!_sources.Any(s => s.IsConfigured)) return summary;

            var due = await _credentials.ListDueForSync(DateTime.UtcNow - SyncInterval);
            summary.TenantsConsidered = due.Count;

            foreach (var credential in due)
            {
                if (ct.IsCancellationRequested) break;
                var result = await Run(credential, ct);
                if (result.Ok) summary.Synced++;
                else if (result.Skipped) summary.Skipped++;
                else summary.Failed++;
            }
            return summary;
        }

        private async Task<DistributorSyncResult> Run(TenantDistributorCredential credential, CancellationToken ct)
        {
            var source = SourceFor(credential.Distributor);
            if (source is null)
            {
                var msg = $"No integration is available for '{credential.Distributor}'.";
                await _credentials.MarkResult(credential.Id, "error", msg, 0, 0);
                return new DistributorSyncResult { Ok = false, Error = msg };
            }
            if (!source.IsConfigured)
            {
                // Not an error against the shop: nothing they did is wrong and nothing they can do
                // fixes it. Recorded so the settings screen can say so plainly.
                var msg = $"{source.DisplayName} syncing isn't switched on for this deployment yet.";
                await _credentials.MarkResult(credential.Id, "error", msg, 0, 0);
                return new DistributorSyncResult { Ok = false, Error = msg, Skipped = true };
            }

            await _credentials.MarkRunning(credential.Id);
            try
            {
                var catalog = await source.FetchCatalog(Decrypt(credential), ct);

                var options = new ShopImportOptions
                {
                    // A sync is a refresh, not a first load: matching and updating is the entire
                    // point, and without this the second night would collide on every unique index.
                    UpdateExisting = true,
                    PresentColumns = catalog.PresentColumns,
                    // The licensing guard, set from the SOURCE so no call site can mislabel a feed
                    // as the shop's own data. See Services.BikeShop.LibraryContribution.
                    ManufacturerNameSource = source.ManufacturerNameSource,
                };

                // byUserId null: nobody clicked anything. Stock movements this creates are
                // attributed to the system rather than to whoever last logged in.
                var result = await _catalog.ImportCatalog(credential.TenantId, catalog.Products, null, options);

                await _credentials.MarkResult(credential.Id, "ok", null,
                    catalog.Products.Count, result.VariantsUpdated);

                _logger.LogInformation(
                    "Distributor sync {Distributor} tenant {TenantId}: {Products} products, {Created} created, {Updated} updated",
                    source.Slug, credential.TenantId, catalog.Products.Count, result.Variants, result.VariantsUpdated);

                return new DistributorSyncResult
                {
                    Ok = true,
                    ProductsSeen = catalog.Products.Count,
                    VariantsCreated = result.Variants,
                    VariantsUpdated = result.VariantsUpdated,
                };
            }
            catch (Exception ex)
            {
                // Message only. A credential failure's detail can carry the account identifier, and
                // this string is shown in the admin UI and written to a column.
                var msg = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                await _credentials.MarkResult(credential.Id, "error", msg, 0, 0);
                _logger.LogError(ex, "Distributor sync failed for {Distributor} tenant {TenantId}",
                    credential.Distributor, credential.TenantId);
                return new DistributorSyncResult { Ok = false, Error = msg };
            }
        }

        private static DistributorCredentials Decrypt(TenantDistributorCredential c) => new(
            c.AccountNumber,
            c.Username,
            string.IsNullOrEmpty(c.PasswordEncrypted) ? null : EncryptionHelper.Decrypt(c.PasswordEncrypted),
            string.IsNullOrEmpty(c.ApiKeyEncrypted) ? null : EncryptionHelper.Decrypt(c.ApiKeyEncrypted));
    }
}
