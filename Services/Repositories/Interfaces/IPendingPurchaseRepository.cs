namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// A Stripe PaymentIntent that still has at least one pending purchase row, with the
    /// oldest created_at across those rows so the reconciler can apply an age policy.
    /// </summary>
    public class PendingPaymentIntent
    {
        public string PaymentIntentId { get; set; } = string.Empty;
        public DateTime OldestCreatedAtUtc { get; set; }
        // Set when this PI was a direct charge on a tenant's own connected account; the reconciler
        // must read the PI status / cancel it on that account, not the platform account. NULL = platform.
        public string? ConnectedAccountId { get; set; }
    }

    /// <summary>
    /// A 'pending' purchase row that never had a PaymentIntent stamped on it (the checkout
    /// died before or during PI creation), so the PI-keyed reconciliation can never see it.
    /// Carries what the abandonment teardown needs: the funding season pass for a
    /// credit-covered ticket and any store credit the checkout redeemed as a tender, both
    /// of which were debited before the PI would have been created and must be handed back.
    /// </summary>
    public class PaymentlessPendingPurchase
    {
        public string TableName { get; set; } = string.Empty;
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        // event_ticket_purchase only: ride credits are burned before the row exists.
        public Guid? AppliedSeasonPassPurchaseId { get; set; }
        // shop_sale / concession_sale only: store credit redeemed as a tender at ring-up.
        public int CreditAppliedCents { get; set; }
    }

    /// <summary>
    /// Read model for the pending-purchase reconciler. Surfaces PaymentIntents whose
    /// purchase rows (event ticket / day pass / extra / membership) are still 'pending'
    /// past a cutoff, so the worker can check Stripe and either finalize them (a missed
    /// webhook) or abandon them (a dead cart, which frees the held inventory). Also
    /// surfaces the PI-less bucket: pending rows the checkout never attached a
    /// PaymentIntent to, which the PI-keyed query is structurally blind to.
    /// </summary>
    public interface IPendingPurchaseRepository
    {
        Task<List<PendingPaymentIntent>> ListStalePendingPaymentIntents(DateTime olderThanUtc, int take = 200);

        /// <summary>
        /// 'pending' rows older than the cutoff with no PaymentIntent, across the same
        /// tables the PI-keyed union covers. Package-composed ticket/rental rows are
        /// excluded: they legitimately ride the package's PI without one of their own.
        /// </summary>
        Task<List<PaymentlessPendingPurchase>> ListStalePendingWithoutPaymentIntent(DateTime olderThanUtc, int take = 200);

        /// <summary>
        /// Flips the given PI-less pending rows of one table to 'abandoned' (gift cards to
        /// 'void', their dead status). Re-checks pending + PI-less inside the UPDATE so a
        /// row that gained a PI after listing is left to the PI reconciliation path.
        /// Returns the ids actually flipped; only those get the abandonment teardown.
        /// </summary>
        Task<List<Guid>> MarkAbandonedWithoutPaymentIntent(string tableName, IReadOnlyList<Guid> ids);
    }
}
