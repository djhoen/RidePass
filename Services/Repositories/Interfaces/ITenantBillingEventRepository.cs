using Services.Repositories.Data.BillingData;

namespace Services.Repositories.Interfaces
{
    public interface ITenantBillingEventRepository
    {
        /// <summary>
        /// Insert a new billable event idempotently. Returns true when the row
        /// was actually inserted, false when (kind, source_id) already exists
        /// — i.e., we already settled this Twilio MessageSID and this is a
        /// retried StatusCallback. Caller treats false as a successful no-op.
        /// </summary>
        Task<bool> RecordIfNew(TenantBillingEvent ev);

        /// <summary>
        /// System-level worklist: events that haven't been attached to a payout
        /// yet (i.e., not yet turned into a tenant_ledger_entry adjustment).
        /// Returns events across all tenants — the caller is the TaskRunner
        /// platform process, not a per-tenant request handler.
        /// </summary>
        Task<List<TenantBillingEvent>> ListPendingPayoutAttach(int limit);

        /// <summary>
        /// Stamp the event as attached: records the tenant_ledger_entry id we
        /// created and the attach time. Operates on the row's PK; called only
        /// from the TaskRunner attach handler that already holds the row.
        /// </summary>
        Task MarkAttachedToPayout(Guid id, Guid payoutEntryId);

        /// <summary>
        /// Sum of billed_cents for a tenant in a date range. Used by the
        /// future Billing &amp; Usage page in the admin UI.
        /// </summary>
        Task<int> SumBilledCents(Guid tenantId, DateTime fromUtc, DateTime toUtc, string? kind = null);
    }
}
