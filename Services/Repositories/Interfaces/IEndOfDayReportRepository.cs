using Services.Repositories.Data.ReportData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Reads behind the End of Day (Z) report and the sales-tax report. Its own repository rather
    /// than more methods on IReportsRepository because everything here reads ONE source,
    /// v_accounting_entries, the same read model the QuickBooks sync posts from. That is the whole
    /// point of the report: the numbers an admin closes the day on and the numbers in the journal
    /// entry come from one place, so they cannot disagree.
    ///
    /// Every method takes tenantId and puts it in the WHERE. Nothing here derives the tenant.
    /// </summary>
    public interface IEndOfDayReportRepository
    {
        /// <summary>
        /// Every accounting row for one tenant-local business date, pre-aggregated by
        /// (entry_kind, source_kind, payment_method). The caller rolls these up into QuickBooks
        /// account buckets.
        /// </summary>
        Task<List<AccountingBucketRow>> GetDayBuckets(Guid tenantId, DateOnly businessDate);

        /// <summary>Per-seller sale/refund totals for the date. Only rows with a sold_by_user_id.</summary>
        Task<List<EndOfDayStaffRow>> GetDayStaff(Guid tenantId, DateOnly businessDate);

        /// <summary>Cash sessions OPENED on the date, in the tenant's own timezone.</summary>
        Task<List<EndOfDayCashSessionRow>> GetDayCashSessions(Guid tenantId, DateOnly businessDate, string timezone);

        /// <summary>Turn-ins SUBMITTED on the date, in the tenant's own timezone.</summary>
        Task<List<EndOfDayTurnInRow>> GetDayCashTurnIns(Guid tenantId, DateOnly businessDate, string timezone);

        /// <summary>
        /// Sales tax on sale and refund rows in a UTC range, grouped by business date, source kind
        /// and entry kind. Refunds carry negative tax so a plain SUM is the net remittable figure.
        /// </summary>
        Task<List<SalesTaxBucketRow>> GetSalesTaxBuckets(Guid tenantId, DateTime fromUtc, DateTime toUtc);

        /// <summary>
        /// Earned revenue on sale and refund rows in a UTC range, grouped by source kind, the event
        /// type's revenue override and entry kind. The caller resolves each group to a QuickBooks
        /// slot and rolls those up into business units. Refunds carry negative gross, so a plain SUM
        /// is net.
        /// </summary>
        Task<List<RevenueBucketRow>> GetRevenueBuckets(Guid tenantId, DateTime fromUtc, DateTime toUtc);
    }
}
