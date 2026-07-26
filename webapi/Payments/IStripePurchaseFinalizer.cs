namespace webapi.Payments
{
    /// <summary>
    /// Finalizes every purchase row attached to a Stripe PaymentIntent: flips pending
    /// rows to paid / failed / abandoned, writes the ledger entries, sends
    /// confirmations, and mints bundled coupons.
    ///
    /// Shared by two callers so there is a single source of truth for fulfillment:
    ///   1. PaymentController.StripeWebhook, the live Stripe event.
    ///   2. PendingPurchaseReconciler, the catch-up sweep that finalizes (or abandons)
    ///      purchases whose webhook was never delivered.
    ///
    /// Must be idempotent: every status flip is guarded on the current status and
    /// every ledger insert swallows the unique-violation, so repeat calls for the
    /// same PaymentIntent are safe.
    /// </summary>
    public interface IStripePurchaseFinalizer
    {
        /// <param name="eventType">
        /// "payment_intent.succeeded" or "payment_intent.payment_failed" as delivered
        /// (or synthesized from the PI's status at Stripe) for real payment outcomes,
        /// or the reconciler-only synthetic "purchase.abandoned": identical teardown to
        /// a failure but the rows land in 'abandoned', preserving the rule that
        /// 'failed' always means Stripe reported a declined payment attempt.
        /// </param>
        Task ProcessPaymentIntentAsync(string paymentIntentId, string eventType, CancellationToken ct = default);

        /// <summary>
        /// Sweeps 'pending' purchase rows that never got a PaymentIntent stamped (the
        /// checkout died before or during PI creation, so the PI-keyed reconciliation
        /// can never see them) once they are older than the cutoff: marks them
        /// 'abandoned' and hands back everything the checkout debited up front (gift
        /// card balance, coupon uses, season pass ride credits, store credit tenders).
        /// Returns how many rows were flipped. Reconciler only.
        /// </summary>
        Task<int> AbandonStalePaymentlessPurchasesAsync(DateTime olderThanUtc, CancellationToken ct = default);

        /// <summary>
        /// Books a checkout credit tender's balancing ledger entry (gross -credit; net -credit
        /// only when reduceNet: platform-mode Stripe rows whose PI collected credit-less; cash
        /// and direct-mode rows already reflect reality). Called by the webhook path for card
        /// checkouts and directly by the controllers for immediately-settled ones. Idempotent.
        /// </summary>
        Task BookCreditTenderEntry(Services.Repositories.Data.CreditData.CheckoutCreditTender tender, bool reduceNet);
    }
}
