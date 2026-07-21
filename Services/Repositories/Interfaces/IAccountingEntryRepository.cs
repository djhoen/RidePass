using Services.Repositories.Data.QuickBooksData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Reads v_accounting_entries (Script0175), the source the QuickBooks sync posts from.</summary>
    public interface IAccountingEntryRepository
    {
        /// <summary>Every accounting row for one tenant on one tenant-local business date.</summary>
        Task<List<AccountingEntry>> ListForBusinessDate(Guid tenantId, DateOnly businessDate);

        /// <summary>
        /// Business dates in [fromDate, toDate] that have any activity for this tenant. Lets the
        /// sync walk only the days that produce a journal entry instead of every date in the gap.
        /// </summary>
        Task<List<DateOnly>> ListBusinessDatesWithActivity(Guid tenantId, DateOnly fromDate, DateOnly toDate);
    }
}
