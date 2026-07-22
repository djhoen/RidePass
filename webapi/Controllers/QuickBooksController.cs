using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Accounting;
using Services.Helpers;
using Services.QuickBooks;
using Services.Repositories.Data.QuickBooksData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.QuickBooks;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Connect a track to QuickBooks Online, map their chart of accounts, and inspect/re-run the
    /// nightly sync. Modeled on the Stripe Connect onboarding flow in TenantController: redirect
    /// out to the provider, land a callback, persist the link, keep status fresh.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class QuickBooksController : ControllerBase
    {
        private readonly IQuickBooksRepository _repo;
        private readonly IQuickBooksTokenService _tokenService;
        private readonly IQuickBooksApiClient _api;
        private readonly IQuickBooksSyncService _sync;
        private readonly ITenantRepository _tenants;
        private readonly ITenantContext _tenantContext;
        private readonly IConfiguration _config;
        private readonly ILogger<QuickBooksController> _logger;

        public QuickBooksController(
            IQuickBooksRepository repo,
            IQuickBooksTokenService tokenService,
            IQuickBooksApiClient api,
            IQuickBooksSyncService sync,
            ITenantRepository tenants,
            ITenantContext tenantContext,
            IConfiguration config,
            ILogger<QuickBooksController> logger)
        {
            _repo = repo;
            _tokenService = tokenService;
            _api = api;
            _sync = sync;
            _tenants = tenants;
            _tenantContext = tenantContext;
            _config = config;
            _logger = logger;
        }

        // ── Status ───────────────────────────────────────────────────────────────────────

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpGet("Status")]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var conn = await _repo.GetConnection(_tenantContext.TenantId);
            var resp = new QuickBooksStatusResponse
            {
                IsConfigured = _tokenService.IsConfigured,
                IsConnected = conn is not null,
                Status = conn?.Status,
                RealmId = conn?.RealmId,
                SyncEnabled = conn?.SyncEnabled ?? false,
                SyncStartDate = conn?.SyncStartDate,
                LastSyncedDate = conn?.LastSyncedDate,
                LastSyncAtUtc = conn?.LastSyncAtUtc,
                LastSyncError = conn?.LastSyncError,
                ConnectedAtUtc = conn?.ConnectedAtUtc,
            };

            if (conn is not null)
            {
                var required = RequiredKeys();
                var mapped = (await _repo.ListMappings(_tenantContext.TenantId))
                    .Select(m => m.MappingKey).ToHashSet(StringComparer.Ordinal);
                resp.UnmappedKeys = required.Where(k => !mapped.Contains(k)).ToList();
                resp.MappingComplete = resp.UnmappedKeys.Count == 0;

                // Best-effort: proves the link still works and shows which company is attached, but
                // a dead link must still render the panel (with its error) rather than 500 the page.
                if (conn.Status == "active")
                {
                    try { resp.CompanyName = await _api.GetCompanyNameAsync(_tenantContext.TenantId, ct); }
                    catch (QuickBooksApiException ex) { resp.LastSyncError ??= ex.Message; }
                }
            }

            return new ApiResponses().OkResult(resp);
        }

        // ── OAuth ────────────────────────────────────────────────────────────────────────

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPost("Connect")]
        public IActionResult Connect()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tokenService.IsConfigured)
            {
                return new ApiResponses().BadRequestResult(
                    "QuickBooks isn't set up on this RidePass deployment yet. Contact support.");
            }

            // Intuit allows only exact, pre-registered redirect URIs (no wildcards), so the callback
            // lands on the apex, where TenantResolutionMiddleware has no subdomain to resolve from.
            // The tenant therefore has to survive the round-trip inside `state`.
            //
            // The state is AES ciphertext with a 15-minute expiry baked in by EncryptionHelper, so a
            // stale or replayed value decrypts to null and is rejected. The GUID makes each state
            // unique despite the helper's fixed IV, so two connects in a row can't collide.
            var state = EncryptionHelper.Encrypt($"{_tenantContext.TenantId}|{Guid.NewGuid()}", TimeSpan.FromMinutes(15));

            return new ApiResponses().OkResult(new QuickBooksConnectResponse
            {
                AuthorizationUrl = _tokenService.BuildAuthorizationUrl(state),
            });
        }

        /// <summary>
        /// Intuit's redirect target. Anonymous and tenant-agnostic by necessity: the browser
        /// arriving here is coming from intuit.com, carries no RidePass JWT, and lands on the apex
        /// host. Authority comes entirely from the signed, expiring `state` minted by Connect.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("Callback")]
        public async Task<IActionResult> Callback(
            [FromQuery] string? code,
            [FromQuery] string? realmId,
            [FromQuery] string? state,
            [FromQuery] string? error,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(state)) return BadRequest("Missing state.");

            var decoded = EncryptionHelper.Decrypt(state);   // null when tampered with or expired
            if (decoded is null)
            {
                return BadRequest("This QuickBooks link expired or was tampered with. Start again from Settings → QuickBooks.");
            }

            var parts = decoded.Split('|');
            if (parts.Length < 1 || !Guid.TryParse(parts[0], out var tenantId))
            {
                return BadRequest("Malformed state.");
            }

            var tenant = await _tenants.GetById(tenantId);
            if (tenant is null) return BadRequest("Unknown track.");

            // Everything from here redirects back to the tenant's own settings screen, so the
            // outcome is visible where the tenant started rather than as raw JSON on the apex.
            var settingsUrl = $"{TenantOrigin(tenant.Subdomain)}/Admin/Settings/QuickBooks";

            // The tenant clicked Cancel/Deny at Intuit.
            if (!string.IsNullOrEmpty(error))
            {
                return Redirect($"{settingsUrl}?qboError={Uri.EscapeDataString(error)}");
            }
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(realmId))
            {
                return Redirect($"{settingsUrl}?qboError={Uri.EscapeDataString("QuickBooks did not return an authorization code.")}");
            }

            var tokens = await _tokenService.ExchangeCodeAsync(code, ct);
            if (tokens is null)
            {
                return Redirect($"{settingsUrl}?qboError={Uri.EscapeDataString("Could not complete the QuickBooks authorization. Please try again.")}");
            }

            // Preserve sync_start_date across a re-auth: it's what stops a reconnect from re-posting
            // history into books that already have it. Only a first-time connect sets it, to today
            // in the tenant's own timezone.
            var existing = await _repo.GetConnection(tenantId);
            var startDate = existing?.SyncStartDate ?? DateOnly.FromDateTime(LocalNow(tenant.Timezone));

            await _repo.UpsertConnection(new QuickBooksConnection
            {
                TenantId = tenantId,
                RealmId = realmId,
                RefreshTokenEncrypted = EncryptionHelper.Encrypt(tokens.RefreshToken, null),
                RefreshTokenExpiresAtUtc = tokens.RefreshExpiresAtUtc,
                AccessTokenEncrypted = EncryptionHelper.Encrypt(tokens.AccessToken, null),
                AccessTokenExpiresAtUtc = tokens.AccessExpiresAtUtc,
                Status = "active",
                SyncEnabled = existing?.SyncEnabled ?? true,
                SyncStartDate = startDate,
                ConnectedByUserId = null,   // no JWT on this hop; the audit trail is connected_at_utc
            });

            _logger.LogInformation("QBO connected for tenant {TenantId} realm {RealmId}", tenantId, realmId);
            return Redirect($"{settingsUrl}?qboConnected=1");
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpDelete("Connect")]
        public async Task<IActionResult> Disconnect(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            await _tokenService.RevokeAsync(_tenantContext.TenantId, ct);
            await _repo.DeleteConnection(_tenantContext.TenantId);
            return new ApiResponses().OkResult(new { disconnected = true });
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPut("SyncEnabled")]
        public async Task<IActionResult> SetSyncEnabled([FromBody] bool enabled)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (await _repo.GetConnection(_tenantContext.TenantId) is null)
                return new ApiResponses().BadRequestResult("QuickBooks isn't connected.");

            await _repo.SetSyncEnabled(_tenantContext.TenantId, enabled);
            return new ApiResponses().OkResult(new { enabled });
        }

        // ── Account mapping ──────────────────────────────────────────────────────────────

        /// <summary>The tenant's chart of accounts, straight from QBO, for the mapping dropdowns.</summary>
        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpGet("Accounts")]
        public async Task<IActionResult> Accounts(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            try
            {
                var accounts = await _api.ListAccountsAsync(_tenantContext.TenantId, ct);
                return new ApiResponses().OkResult(accounts.Select(a => new QboAccountResponse
                {
                    Id = a.Id,
                    Name = a.Name,
                    AccountType = a.AccountType,
                    AccountSubType = a.AccountSubType,
                    Classification = a.Classification,
                }).ToList());
            }
            catch (QuickBooksApiException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpGet("Mappings")]
        public async Task<IActionResult> Mappings()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var saved = (await _repo.ListMappings(_tenantContext.TenantId))
                .ToDictionary(m => m.MappingKey, StringComparer.Ordinal);

            // Return every slot this tenant actually needs, mapped or not, so the UI renders the
            // full form from one call and can show what's still missing.
            var rows = RequiredKeys().Select(key => new QboMappingResponse
            {
                MappingKey = key,
                Label = QboAccountKeys.Label(key),
                ExpectedClassification = ClassificationFor(key),
                QboAccountId = saved.TryGetValue(key, out var m) ? m.QboAccountId : null,
                QboAccountName = saved.TryGetValue(key, out var m2) ? m2.QboAccountName : null,
            }).ToList();

            return new ApiResponses().OkResult(rows);
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPut("Mappings")]
        public async Task<IActionResult> SaveMappings([FromBody] SaveQboMappingsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            foreach (var item in req.Mappings ?? new List<QboMappingItem>())
            {
                // Reject unknown keys outright: a typo'd key would save silently and then read as an
                // unmapped slot forever, which looks like the save didn't work.
                if (!QboAccountKeys.All.Contains(item.MappingKey, StringComparer.Ordinal))
                {
                    return new ApiResponses().BadRequestResult($"Unknown account slot \"{item.MappingKey}\".");
                }

                if (string.IsNullOrWhiteSpace(item.QboAccountId))
                {
                    await _repo.DeleteMapping(_tenantContext.TenantId, item.MappingKey);
                }
                else
                {
                    await _repo.UpsertMapping(_tenantContext.TenantId, item.MappingKey, item.QboAccountId.Trim(), item.QboAccountName);
                }
            }

            return new ApiResponses().OkResult(new { saved = true });
        }

        // ── Sync ─────────────────────────────────────────────────────────────────────────

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpGet("SyncLog")]
        public async Task<IActionResult> SyncLog([FromQuery] int take = 60)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var rows = await _repo.ListSyncLog(_tenantContext.TenantId, Math.Clamp(take, 1, 365));
            return new ApiResponses().OkResult(rows.Select(r => new QboSyncLogResponse
            {
                BusinessDate = r.BusinessDate,
                Status = r.Status,
                QboJournalEntryId = r.QboJournalEntryId,
                QboDocNumber = r.QboDocNumber,
                EntryCount = r.EntryCount,
                TotalDebitsCents = r.TotalDebitsCents,
                AttemptCount = r.AttemptCount,
                LastError = r.LastError,
                SyncedAtUtc = r.SyncedAtUtc,
            }).ToList());
        }

        /// <summary>Catch up every outstanding day now instead of waiting for tonight's sweep.</summary>
        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPost("Sync")]
        public async Task<IActionResult> SyncNow(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var results = await _sync.SyncTenantAsync(_tenantContext.TenantId, ct);
            var failed = results.FirstOrDefault(r => r.Status == "failed");
            if (failed is not null)
            {
                return new ApiResponses().BadRequestResult($"{failed.BusinessDate:yyyy-MM-dd} could not be posted: {failed.Error}");
            }

            return new ApiResponses().OkResult(new
            {
                posted = results.Count(r => r.Status == "success"),
                skipped = results.Count(r => r.Status == "no_activity"),
            });
        }

        /// <summary>
        /// Re-run one business date after fixing what made it fail. A day that already posted is
        /// reported as such rather than posted again, the sync log's unique index makes that
        /// impossible regardless, but saying so plainly beats a silent no-op.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPost("Resync")]
        public async Task<IActionResult> Resync([FromBody] ResyncQboDateRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var existing = await _repo.GetSyncLog(_tenantContext.TenantId, req.BusinessDate);
            if (existing?.Status == "success")
            {
                return new ApiResponses().BadRequestResult(
                    $"{req.BusinessDate:yyyy-MM-dd} was already posted to QuickBooks as {existing.QboDocNumber ?? existing.QboJournalEntryId}. " +
                    "Delete that journal entry in QuickBooks first if you need to re-post it.");
            }

            var result = await _sync.SyncBusinessDateAsync(_tenantContext.TenantId, req.BusinessDate, ct);
            if (result.Status == "failed")
            {
                return new ApiResponses().BadRequestResult(result.Error ?? "The re-sync failed.");
            }
            return new ApiResponses().OkResult(new { status = result.Status, journalEntryId = result.JournalEntryId });
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The account slots THIS tenant can actually touch. Gated by their feature toggles and
        /// charge mode so the mapping form doesn't demand an account for a subsystem they've never
        /// turned on, an unmapped slot blocks a day's post, so asking for slots they can't use
        /// would be a permanent, self-inflicted blocker.
        /// </summary>
        private List<string> RequiredKeys()
        {
            var t = _tenantContext.Tenant;
            var keys = new List<string> { QboAccountKeys.RevenueEventTicket, QboAccountKeys.RevenueOther };

            if (t.ExtrasEnabled)       keys.Add(QboAccountKeys.RevenueEventExtra);
            if (t.SeasonPassesEnabled) keys.Add(QboAccountKeys.RevenueSeasonPass);
            if (t.MembershipEnabled)   keys.Add(QboAccountKeys.RevenueMembership);
            if (t.ConcessionsEnabled)  keys.Add(QboAccountKeys.RevenueConcession);
            if (t.RentalsEnabled)
            {
                keys.Add(QboAccountKeys.RevenueRental);
                keys.Add(QboAccountKeys.RevenueDepositForfeited);
                keys.Add(QboAccountKeys.LiabilityRentalDeposit);
            }
            if (t.GiftCardsEnabled) keys.Add(QboAccountKeys.LiabilityGiftCard);

            // Tax and tips can appear on any tenant: admission tax is configured independently of
            // concessions, and a tip can ride any concession sale. Always required.
            keys.Add(QboAccountKeys.LiabilitySalesTax);
            if (t.ConcessionsEnabled) keys.Add(QboAccountKeys.LiabilityTips);

            keys.Add(QboAccountKeys.AssetUndepositedCash);
            keys.Add(QboAccountKeys.ExpenseRidepassFees);

            // Required for EVERY tenant, including direct-charge ones. It's tempting to treat the
            // receivable as platform-mode-only, but a direct tenant still hits it constantly:
            //   • every cash sale books net = -cut (they hold the money and owe us our cut),
            //   • SMS and email campaign charges are billed the same way regardless of mode.
            // Gating it on mode would leave a direct tenant's first cash sale unable to post, with
            // "no account is mapped for RidePass receivable" and no way to map it.
            keys.Add(QboAccountKeys.AssetRidepassReceivable);
            keys.Add(QboAccountKeys.ExpenseStripeFees);

            // Direct charge only: the tenant is merchant of record, so card money lands in their own
            // Stripe balance instead of becoming a RidePass payout. A platform tenant never emits a
            // line here, so asking them to map it would be noise.
            if (t.StripeChargeMode == "direct")
            {
                keys.Add(QboAccountKeys.AssetStripeClearing);
            }

            return keys.Distinct(StringComparer.Ordinal)
                       .OrderBy(k => Array.IndexOf(QboAccountKeys.All, k))
                       .ToList();
        }

        /// <summary>QBO's Account.Classification the slot expects, so the UI filters the dropdown.</summary>
        private static string ClassificationFor(string key) =>
            key.StartsWith("revenue_", StringComparison.Ordinal)   ? "Revenue"
            : key.StartsWith("liability_", StringComparison.Ordinal) ? "Liability"
            : key.StartsWith("asset_", StringComparison.Ordinal)     ? "Asset"
            : "Expense";

        private string TenantOrigin(string subdomain)
        {
            var apex = _config["App:RootDomain"] ?? "ridepass.io";
            return $"https://{subdomain}.{apex}";
        }

        private static DateTime LocalNow(string? iana)
        {
            if (string.IsNullOrWhiteSpace(iana)) return DateTime.UtcNow;
            try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(iana)); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return DateTime.UtcNow; }
        }
    }
}
