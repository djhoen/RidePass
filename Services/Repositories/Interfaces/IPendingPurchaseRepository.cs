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
    }

    /// <summary>
    /// Read model for the pending-purchase reconciler. Surfaces PaymentIntents whose
    /// purchase rows (event ticket / day pass / extra / membership) are still 'pending'
    /// past a cutoff, so the worker can check Stripe and either finalize them (a missed
    /// webhook) or fail them (an abandoned cart, which frees the held inventory).
    /// </summary>
    public interface IPendingPurchaseRepository
    {
        Task<List<PendingPaymentIntent>> ListStalePendingPaymentIntents(DateTime olderThanUtc, int take = 200);
    }
}
