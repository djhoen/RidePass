using Microsoft.Extensions.Logging;
using Services.Accounting;
using Services.Repositories.Data.QuickBooksData;
using Services.Repositories.Interfaces;

namespace Services.QuickBooks
{
    /// <summary>Outcome of posting one business date.</summary>
    public record QboDayResult(DateOnly BusinessDate, string Status, string? JournalEntryId, string? Error)
    {
        public bool Posted => Status == "success";
    }

    public record QboSweepSummary(int TenantsConsidered, int DaysPosted, int DaysSkipped, int DaysFailed);

    public interface IQuickBooksSyncService
    {
        /// <summary>Post one business date for one tenant. Idempotent, a day already posted is skipped.</summary>
        Task<QboDayResult> SyncBusinessDateAsync(Guid tenantId, DateOnly businessDate, CancellationToken ct = default);
        /// <summary>Catch a single tenant up from its cursor to its last complete local day.</summary>
        Task<List<QboDayResult>> SyncTenantAsync(Guid tenantId, CancellationToken ct = default);
        /// <summary>The nightly cross-tenant sweep.</summary>
        Task<QboSweepSummary> SyncDueTenantsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Drives the nightly post. Reads a tenant-local day out of v_accounting_entries, folds it into
    /// one balanced journal via JournalEntryBuilder, and posts it to the tenant's QBO company.
    ///
    /// The one rule that governs everything here: NEVER post the same business date twice. Double-
    /// posting revenue into a customer's live books is the worst thing this feature can do, and it
    /// is not something they'd notice quickly. So the guarantee lives in the database, the unique
    /// index on qbo_sync_log (tenant_id, business_date), claimed via TryClaimBusinessDate before any
    /// QBO call, rather than in a flag or an in-process lock.
    ///
    /// A day is only posted once it is COMPLETE in the tenant's own timezone. Posting "today" would
    /// book a partial day and then have no way to add the rest, because the date is already claimed.
    /// </summary>
    public class QuickBooksSyncService : IQuickBooksSyncService
    {
        private readonly IQuickBooksRepository _repo;
        private readonly IAccountingEntryRepository _entries;
        private readonly IQuickBooksApiClient _api;
        private readonly ITenantRepository _tenants;
        private readonly ILogger<QuickBooksSyncService> _logger;

        public QuickBooksSyncService(
            IQuickBooksRepository repo,
            IAccountingEntryRepository entries,
            IQuickBooksApiClient api,
            ITenantRepository tenants,
            ILogger<QuickBooksSyncService> logger)
        {
            _repo = repo;
            _entries = entries;
            _api = api;
            _tenants = tenants;
            _logger = logger;
        }

        public async Task<QboSweepSummary> SyncDueTenantsAsync(CancellationToken ct = default)
        {
            var connections = await _repo.ListSyncableConnections();
            int posted = 0, skipped = 0, failed = 0;

            foreach (var conn in connections)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    // Every downstream call is scoped by this row's own TenantId, the sweep is the
                    // only cross-tenant read in the feature, and it fans back out per tenant here.
                    var results = await SyncTenantAsync(conn.TenantId, ct);
                    posted  += results.Count(r => r.Status == "success");
                    skipped += results.Count(r => r.Status == "no_activity");
                    failed  += results.Count(r => r.Status == "failed");
                }
                catch (Exception ex)
                {
                    // One tenant's bad connection must never stop the sweep for everyone else.
                    failed++;
                    _logger.LogError(ex, "QBO sweep failed for tenant {TenantId}", conn.TenantId);
                    await _repo.SetStatus(conn.TenantId, "error", Truncate(ex.Message));
                }
            }

            return new QboSweepSummary(connections.Count, posted, skipped, failed);
        }

        public async Task<List<QboDayResult>> SyncTenantAsync(Guid tenantId, CancellationToken ct = default)
        {
            var results = new List<QboDayResult>();

            var conn = await _repo.GetConnection(tenantId);
            if (conn is null || !conn.SyncEnabled || conn.Status != "active") return results;

            var tenant = await _tenants.GetById(tenantId);
            if (tenant is null) return results;

            // The last day that is fully over where the track actually is. A Saturday night gate
            // take is still landing at 02:00 UTC Sunday, so a UTC-based cutoff would post Saturday
            // while it was still selling.
            var tz = ResolveTimeZone(tenant.Timezone);
            var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
            var lastCompleteDay = localToday.AddDays(-1);

            // Start the day after the cursor, or at the connect date if we've never posted. Never
            // before sync_start_date: linking an account must not dump history into live books.
            var from = conn.LastSyncedDate.HasValue ? conn.LastSyncedDate.Value.AddDays(1) : conn.SyncStartDate;
            if (from < conn.SyncStartDate) from = conn.SyncStartDate;
            if (from > lastCompleteDay) return results;   // already current

            // Walk only the days that actually produced money, not every date in the gap, a track
            // that ran one event last month shouldn't cost 30 no-op posts.
            var dates = await _entries.ListBusinessDatesWithActivity(tenantId, from, lastCompleteDay);

            foreach (var date in dates)
            {
                if (ct.IsCancellationRequested) break;
                results.Add(await SyncBusinessDateAsync(tenantId, date, ct));
            }

            // Advance the cursor to the last complete day even when some dates had no activity, so
            // we don't re-scan the same empty range every night. Only do this if nothing failed, // moving the cursor past a failed day would silently skip it forever.
            if (!results.Any(r => r.Status == "failed"))
            {
                await _repo.SetSyncCursor(tenantId, lastCompleteDay, DateTime.UtcNow);
            }

            return results;
        }

        public async Task<QboDayResult> SyncBusinessDateAsync(Guid tenantId, DateOnly businessDate, CancellationToken ct = default)
        {
            // Claim first. If this day already posted successfully, stop here, before any QBO call.
            if (!await _repo.TryClaimBusinessDate(tenantId, businessDate))
            {
                var existing = await _repo.GetSyncLog(tenantId, businessDate);
                return new QboDayResult(businessDate, "success", existing?.QboJournalEntryId,
                    "Already posted to QuickBooks; skipped to avoid double-posting.");
            }

            try
            {
                var entries = await _entries.ListForBusinessDate(tenantId, businessDate);
                if (entries.Count == 0)
                {
                    await Record(tenantId, businessDate, "no_activity", null, null, 0, 0, null);
                    return new QboDayResult(businessDate, "no_activity", null, null);
                }

                // No tenant lookup needed: platform-vs-direct is read per entry from the payment_method
                // the finalizer snapshotted at charge time, not from the tenant's current mode.
                var draft = JournalEntryBuilder.Build(entries, businessDate);
                if (draft.IsEmpty)
                {
                    // Real activity that nets to nothing, e.g. a sale and its full refund on the
                    // same day. Correct to post nothing; QBO rejects zero-amount lines anyway.
                    await Record(tenantId, businessDate, "no_activity", null, null, entries.Count, 0, null);
                    return new QboDayResult(businessDate, "no_activity", null, null);
                }

                var mappings = await _repo.ListMappings(tenantId);
                var accountIds = mappings.ToDictionary(m => m.MappingKey, m => m.QboAccountId, StringComparer.Ordinal);

                // Deterministic and unique per (tenant, day) so a human can find the entry in QBO
                // from the RidePass sync log, and so an accidental duplicate is obvious on sight.
                var docNumber = $"RP-{businessDate:yyyyMMdd}";

                var posted = await _api.CreateJournalEntryAsync(tenantId, draft, accountIds, docNumber, ct);

                await Record(tenantId, businessDate, "success", posted.Id, posted.DocNumber,
                    draft.EntryCount, draft.TotalDebitCents, null);

                _logger.LogInformation("QBO posted {Date} for tenant {TenantId} as JE {JeId} ({Lines} lines, ${Total:0.00})",
                    businessDate, tenantId, posted.Id, draft.Lines.Count, draft.TotalDebitCents / 100m);

                return new QboDayResult(businessDate, "success", posted.Id, null);
            }
            catch (Exception ex)
            {
                // The claim row stays 'failed', so tonight's sweep retries it and the cursor won't
                // advance past it. The message is surfaced verbatim on the settings screen, so it
                // has to read like something a track owner can act on.
                var message = ex switch
                {
                    QuickBooksApiException qex => qex.Message,
                    JournalImbalanceException => "RidePass could not build a balanced journal entry for this day. " +
                                                 "This is a bug, the day was not posted. Please contact support.",
                    _ => $"Unexpected error posting to QuickBooks: {ex.Message}",
                };
                await Record(tenantId, businessDate, "failed", null, null, 0, 0, Truncate(message));
                await _repo.SetStatus(tenantId, "error", Truncate(message));
                _logger.LogError(ex, "QBO post failed for tenant {TenantId} date {Date}", tenantId, businessDate);
                return new QboDayResult(businessDate, "failed", null, message);
            }
        }

        private Task Record(Guid tenantId, DateOnly date, string status, string? jeId, string? docNumber,
                            int entryCount, long totalDebits, string? error) =>
            _repo.RecordSyncOutcome(new QboSyncLogEntry
            {
                TenantId = tenantId,
                BusinessDate = date,
                Status = status,
                QboJournalEntryId = jeId,
                QboDocNumber = docNumber,
                EntryCount = entryCount,
                TotalDebitsCents = totalDebits,
                LastError = error,
                SyncedAtUtc = status == "success" ? DateTime.UtcNow : null,
            });

        /// <summary>
        /// tenant.timezone is an IANA id. Falling back to UTC rather than throwing keeps one
        /// mis-configured tenant from stalling the sweep; the day boundary is just less exact.
        /// </summary>
        private TimeZoneInfo ResolveTimeZone(string? iana)
        {
            if (string.IsNullOrWhiteSpace(iana)) return TimeZoneInfo.Utc;
            try { return TimeZoneInfo.FindSystemTimeZoneById(iana); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                _logger.LogWarning("Unknown tenant timezone '{Tz}'; falling back to UTC for the QBO day boundary", iana);
                return TimeZoneInfo.Utc;
            }
        }

        private static string Truncate(string s) => s.Length > 1000 ? s[..1000] : s;
    }
}
