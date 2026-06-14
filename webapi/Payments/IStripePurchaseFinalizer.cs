namespace webapi.Payments
{
    /// <summary>
    /// Finalizes every purchase row attached to a Stripe PaymentIntent: flips pending
    /// rows to paid / failed, writes the ledger entries, sends confirmations, mints
    /// bundled coupons, and runs the reward engine.
    ///
    /// Shared by two callers so there is a single source of truth for fulfillment:
    ///   1. PaymentController.StripeWebhook — the live Stripe event.
    ///   2. PendingPurchaseReconciler — the catch-up sweep that finalizes (or fails)
    ///      purchases whose webhook was never delivered.
    ///
    /// Must be idempotent: every status flip is guarded on the current status and
    /// every ledger insert swallows the unique-violation, so repeat calls for the
    /// same PaymentIntent are safe.
    /// </summary>
    public interface IStripePurchaseFinalizer
    {
        /// <param name="eventType">
        /// "payment_intent.succeeded" or "payment_intent.payment_failed" — the
        /// reconciler synthesizes this from the PaymentIntent's status at Stripe.
        /// </param>
        Task ProcessPaymentIntentAsync(string paymentIntentId, string eventType, CancellationToken ct = default);
    }
}
