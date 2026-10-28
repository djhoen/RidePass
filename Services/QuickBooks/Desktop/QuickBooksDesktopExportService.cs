using Microsoft.Extensions.Logging;
using Services.Accounting;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.QuickBooksData;
using Services.Repositories.Interfaces;

namespace Services.QuickBooks.Desktop
{
    /// <summary>A refusal the track can act on; the message is shown on the settings screen verbatim.</summary>
    public sealed class QbdExportException : Exception
    {
        public QbdExportException(string message) : base(message) { }
    }

    /// <param name="BatchId">Null when there was nothing new to export.</param>
    /// <param name="MoreRemaining">True when more closed days are waiting than one file holds.</param>
    public sealed record QbdExportResult(Guid? BatchId, int DaysInFile, int DaysWithNoActivity,
        DateOnly? FromDate, DateOnly? ToDate, DateOnly LastCompleteDay, bool MoreRemaining);

    public interface IQuickBooksDesktopExportService
    {
        /// <summary>
        /// Build one IIF file covering every closed business day not yet exported or posted, and
        /// claim those days. All or nothing: any day that can't be built cleanly fails the export.
        /// </summary>
        Task<QbdExportResult> ExportAsync(Guid tenantId, Guid? userId, CancellationToken ct = default);
    }

    /// <summary>
    /// The Desktop half of the accounting sync. Same input (v_accounting_entries), same builder
    /// (JournalEntryBuilder), same per-day idempotency anchor (qbo_sync_log), different delivery:
    /// a file the bookkeeper imports instead of an API post.
    ///
    /// The governing rule is the Online sync's, made stricter by what Desktop can't tell us: never
    /// put the same business date in two files. We can't see an import happen, so a day counts as
    /// done the moment it lands in a batch, and the only way to release it is voiding that batch.
    /// And because an IIF import CREATES any account or class it doesn't recognise, every line is
    /// checked against the track's own uploaded lists before a byte is written; a mismatch fails
    /// the export instead of growing junk accounts in their books.
    /// </summary>
    public class QuickBooksDesktopExportService : IQuickBooksDesktopExportService
    {
        /// <summary>One file holds at most a year of days, so a long-dormant setup can't build a giant file.</summary>
        public const int MaxDaysPerFile = 366;

        private readonly IQuickBooksDesktopRepository _repo;
        private readonly IAccountingEntryRepository _entries;
        private readonly ITenantRepository _tenants;
        private readonly IProfitCenterRepository _profitCenters;
        private readonly IDbHelper _db;
        private readonly ILogger<QuickBooksDesktopExportService> _logger;

        public QuickBooksDesktopExportService(
            IQuickBooksDesktopRepository repo,
            IAccountingEntryRepository entries,
            ITenantRepository tenants,
            IProfitCenterRepository profitCenters,
            IDbHelper db,
            ILogger<QuickBooksDesktopExportService> logger)
        {
            _repo = repo;
            _entries = entries;
            _tenants = tenants;
            _profitCenters = profitCenters;
            _db = db;
            _logger = logger;
        }

        public async Task<QbdExportResult> ExportAsync(Guid tenantId, Guid? userId, CancellationToken ct = default)
        {
            var setup = await _repo.GetSetup(tenantId)
                ?? throw new QbdExportException("QuickBooks Desktop isn't set up for this track.");
            var tenant = await _tenants.GetById(tenantId)
                ?? throw new QbdExportException("Track not found.");

            // Same day boundary as the Online sweep: only days fully over where the track is.
            var tz = ResolveTimeZone(tenant.Timezone);
            var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
            var lastCompleteDay = localToday.AddDays(-1);

            var accountList = await _repo.ListItems(tenantId, "account");
            if (accountList.Count == 0)
            {
                throw new QbdExportException("Upload your Chart of Accounts from QuickBooks Desktop before exporting.");
            }

            // Serialise exports per tenant: two clicks (or two admins) must not each claim the same
            // days into two different files. CreateBatch re-checks every claim as a backstop.
            await using var exportLock = await _db.AcquireAdvisoryLock($"qbd-export:{tenantId}");

            if (setup.SyncStartDate > lastCompleteDay)
            {
                return new QbdExportResult(null, 0, 0, null, null, lastCompleteDay, false);
            }

            var posted = await _repo.ListSettledDates(tenantId, setup.SyncStartDate, lastCompleteDay);
            var pending = (await _entries.ListBusinessDatesWithActivity(tenantId, setup.SyncStartDate, lastCompleteDay))
                .Where(d => !posted.Contains(d))
                .OrderBy(d => d)
                .ToList();
            var moreRemaining = pending.Count > MaxDaysPerFile;
            pending = pending.Take(MaxDaysPerFile).ToList();

            if (pending.Count == 0)
            {
                return new QbdExportResult(null, 0, 0, null, null, lastCompleteDay, false);
            }

            var accountMappings = await _repo.ListAccountMappings(tenantId);
            var classMappings = await _repo.ListClassMappings(tenantId);
            var classList = await _repo.ListItems(tenantId, "class");

            var errors = new List<string>();
            ValidateClasses(classMappings, classList, errors);

            var classBySlot = classMappings.Count == 0
                ? null
                : ClassAssignment.ForRevenueSlots(
                    ProfitCenterMap.FromConfig(
                        await _profitCenters.ListForTenant(tenantId),
                        await _profitCenters.ListAssignments(tenantId)),
                    classMappings);

            var accountsByName = accountList.ToDictionary(a => a.FullName, StringComparer.Ordinal);
            var badSlots = new HashSet<string>(StringComparer.Ordinal);
            var drafts = new List<JournalDraft>();
            var days = new List<QbdExportDay>();

            foreach (var date in pending)
            {
                ct.ThrowIfCancellationRequested();
                var entries = await _entries.ListForBusinessDate(tenantId, date);
                JournalDraft draft;
                try
                {
                    draft = JournalEntryBuilder.Build(entries, date, classBySlot);
                }
                catch (JournalImbalanceException ex)
                {
                    _logger.LogError(ex, "QBD export: unbalanced journal for tenant {TenantId} date {Date}", tenantId, date);
                    errors.Add($"RidePass could not build a balanced journal entry for {date:MM/dd/yyyy}. This is a bug; " +
                               "nothing was exported. Please contact support.");
                    continue;
                }

                if (draft.IsEmpty)
                {
                    // A sale and its full refund on one day: real activity, nothing to post.
                    days.Add(new QbdExportDay(date, "no_activity", entries.Count, 0));
                    continue;
                }

                foreach (var line in draft.Lines)
                {
                    if (badSlots.Contains(line.AccountKey)) continue;
                    var problem = AccountProblem(line.AccountKey, accountMappings, accountsByName);
                    if (problem is not null)
                    {
                        badSlots.Add(line.AccountKey);
                        errors.Add(problem);
                    }
                }

                drafts.Add(draft);
                days.Add(new QbdExportDay(date, "success", draft.EntryCount, draft.TotalDebitCents));
            }

            if (errors.Count > 0)
            {
                throw new QbdExportException("Nothing was exported. " + string.Join(" ", errors.Distinct()));
            }

            var exported = days.Where(d => d.Status == "success").ToList();
            if (exported.Count == 0)
            {
                // Every pending day netted to zero. Record them so they aren't rebuilt next time,
                // but there is no file to hand over, so no batch either.
                try { await _repo.CreateBatch(tenantId, null, days); }
                catch (InvalidOperationException ex) { throw new QbdExportException(ex.Message); }
                return new QbdExportResult(null, 0, days.Count, null, null, lastCompleteDay, moreRemaining);
            }

            string content;
            try
            {
                content = IifJournalWriter.Write(drafts, accountMappings);
            }
            catch (IifWriteException ex)
            {
                throw new QbdExportException("Nothing was exported. " + ex.Message);
            }

            var batch = new QbdExportBatch
            {
                TenantId = tenantId,
                FromDate = exported.Min(d => d.BusinessDate),
                ToDate = exported.Max(d => d.BusinessDate),
                DayCount = exported.Count,
                TotalDebitsCents = exported.Sum(d => d.TotalDebitsCents),
                FileContent = content,
                CreatedByUserId = userId,
            };

            Guid? batchId;
            try
            {
                batchId = await _repo.CreateBatch(tenantId, batch, days);
            }
            catch (InvalidOperationException ex)
            {
                throw new QbdExportException(ex.Message);
            }

            _logger.LogInformation("QBD export {BatchId} for tenant {TenantId}: {Days} days {From}..{To}, ${Total:0.00}",
                batchId, tenantId, batch.DayCount, batch.FromDate, batch.ToDate, batch.TotalDebitsCents / 100m);

            return new QbdExportResult(batchId, exported.Count, days.Count - exported.Count,
                batch.FromDate, batch.ToDate, lastCompleteDay, moreRemaining);
        }

        /// <summary>
        /// Why a slot can't be written, or null when it can. Each reason names the fix, because the
        /// track's only lever is this settings screen and their own QuickBooks file.
        /// </summary>
        private static string? AccountProblem(string slot, IReadOnlyDictionary<string, string> mappings,
            IReadOnlyDictionary<string, QbdListItem> accountsByName)
        {
            var label = QboAccountKeys.Label(slot);
            if (!mappings.TryGetValue(slot, out var name))
            {
                return $"No account is mapped for \"{label}\"; set it in the Chart of accounts tab.";
            }
            if (!accountsByName.TryGetValue(name, out var account))
            {
                return $"\"{label}\" is mapped to \"{name}\", which isn't in the chart of accounts you uploaded. " +
                       "Upload a fresh list or pick another account.";
            }
            if (account.IsHidden)
            {
                return $"\"{label}\" is mapped to \"{name}\", which is inactive in QuickBooks Desktop. " +
                       "Make it active there or pick another account.";
            }
            if (account.AccountType is not null && !IifListParser.IsPostable(account.AccountType))
            {
                return $"\"{label}\" is mapped to \"{name}\", a {IifListParser.TypeLabel(account.AccountType)} account. " +
                       "QuickBooks needs a customer or vendor on those lines, so pick a different account.";
            }
            return null;
        }

        private static void ValidateClasses(IReadOnlyDictionary<string, string> classMappings,
            IReadOnlyList<QbdListItem> classList, List<string> errors)
        {
            var classes = classList.ToDictionary(c => c.FullName, StringComparer.Ordinal);
            foreach (var name in classMappings.Values.Distinct(StringComparer.Ordinal))
            {
                // A class name QuickBooks doesn't know would be CREATED by the import, so a stale
                // mapping blocks the file rather than being dropped or passed through.
                if (!classes.TryGetValue(name, out var c))
                {
                    errors.Add($"The class \"{name}\" isn't in the class list you uploaded. Upload a fresh list or change it in the Profit centers tab.");
                }
                else if (c.IsHidden)
                {
                    errors.Add($"The class \"{name}\" is inactive in QuickBooks Desktop. Make it active there or change it in the Profit centers tab.");
                }
            }
        }

        /// <summary>Same fallback as the Online sync: an unknown zone means UTC, never a stalled export.</summary>
        private TimeZoneInfo ResolveTimeZone(string? iana)
        {
            if (string.IsNullOrWhiteSpace(iana)) return TimeZoneInfo.Utc;
            try { return TimeZoneInfo.FindSystemTimeZoneById(iana); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                _logger.LogWarning("Unknown tenant timezone '{Tz}'; falling back to UTC for the QuickBooks Desktop day boundary", iana);
                return TimeZoneInfo.Utc;
            }
        }
    }
}
