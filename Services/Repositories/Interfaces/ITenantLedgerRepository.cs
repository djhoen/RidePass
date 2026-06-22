using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface ITenantLedgerRepository
    {
        Task<Guid> Insert(TenantLedgerEntry entry);

        Task<List<TenantLedgerEntry>> ListByTenant(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, int take = 200);

        /// <summary>
        /// Sum of gross_cents (entry_kind='sale') for the UTC calendar month containing <paramref name="atUtc"/>,
        /// up to but not including atUtc itself. Used by the FeeCalculator for tier determination.
        /// </summary>
        Task<long> GetMonthlyGrossVolumeCents(Guid tenantId, DateTime atUtc);

        Task<TenantBalanceSummary?> GetSummary(Guid tenantId);

        Task<List<TenantBalanceSummary>> GetSummariesForAllTenants();

        /// <summary>
        /// Sum of stripe-fee withholdings for the current UTC month — used to enforce monthly cap.
        /// </summary>
        Task<long> GetMonthlyRidepassCutCents(Guid tenantId, DateTime atUtc);

        /// <summary>
        /// Look up the original 'sale' ledger entry for a source purchase. Used by the refund flow
        /// to mirror the sale's amounts as a negative refund entry.
        /// </summary>
        Task<TenantLedgerEntry?> GetSaleEntryForSource(Guid tenantId, string sourceKind, Guid sourceId);

        /// <summary>
        /// Period totals across all tenants. Used by the reconciliation view to compare against Stripe.
        /// </summary>
        Task<LedgerPeriodTotals> SumForPeriod(DateTime fromUtc, DateTime toUtc);

        /// <summary>Net cash (sales minus refunds) a worker handled in a window — the basis
        /// for a cash turn-in's expected drawer. Refund rows carry negative gross.</summary>
        Task<long> SumCashNetForWorker(Guid tenantId, Guid workerUserId, DateTime fromUtc, DateTime toUtc);

        /// <summary>Refund volume per worker over a window, split cash vs card.</summary>
        Task<List<WorkerRefundTotals>> ListRefundsByWorker(Guid tenantId, DateTime fromUtc, DateTime toUtc);
    }

    public record LedgerPeriodTotals(int Count, long GrossCents, long StripeFeeCents, long RidepassCutCents, long NetToTenantCents);

    public record WorkerRefundTotals(Guid WorkerUserId, int CashCount, long CashCents, int CardCount, long CardCents);
}
