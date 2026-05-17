using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface ITenantPayoutRepository
    {
        Task<List<TenantPayout>> ListByTenant(Guid tenantId, int take = 50);
        Task<TenantPayout?> GetById(Guid id, Guid tenantId);
        Task<TenantPayout?> GetByExternalReference(string externalReference);
        Task<List<TenantLedgerEntry>> ListEntriesForPayout(Guid payoutId);

        Task<Guid> Create(TenantPayout payout);

        /// <summary>
        /// Attach all unpaid ledger entries for the tenant in the given UTC period to this payout.
        /// Returns the count of entries attached.
        /// </summary>
        Task<int> AttachUnpaidEntries(Guid payoutId, Guid tenantId, DateTime fromUtc, DateTime toUtc);

        /// <summary>
        /// Recompute totals on a payout by summing its attached ledger entries. Call after AttachUnpaidEntries.
        /// </summary>
        Task RefreshTotals(Guid payoutId);

        Task UpdateStatus(Guid id, Guid tenantId, string status, DateTime? payoutDateUtc, string? externalReference, string? memo, Guid? approvedByUserId);

        /// <summary>
        /// Void a pending payout: detach all its ledger entries (set payout_id=NULL) and delete the payout row.
        /// Refuses to void anything past 'pending'. Returns true on success.
        /// </summary>
        Task<bool> Void(Guid payoutId, Guid tenantId);
    }
}
