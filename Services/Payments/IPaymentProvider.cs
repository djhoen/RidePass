namespace Services.Payments
{
    public interface IPaymentProvider
    {
        Task<PaymentIntentCreated> CreatePaymentIntentAsync(
            long amountCents,
            string currency,
            IReadOnlyDictionary<string, string> metadata,
            string? receiptEmail = null,
            CancellationToken ct = default);

        PaymentWebhookEvent? VerifyAndParseWebhook(string rawBody, string signatureHeader);

        Task<RefundResult> RefundAsync(string paymentIntentId, long? amountCents = null, CancellationToken ct = default);

        /// <summary>
        /// Returns the actual processor fee (in cents) Stripe charged for the latest charge on this
        /// PaymentIntent. Returns null if the PI is not yet captured / settled, or if Stripe credentials
        /// are not configured.
        /// </summary>
        Task<int?> GetActualStripeFeeCentsAsync(string paymentIntentId, CancellationToken ct = default);

        /// <summary>
        /// Sums Stripe balance_transactions in [fromUtc, toUtc). Used by the reconciliation view to
        /// compare what Stripe actually credited/debited against what we recorded in our ledger.
        /// Returns null when Stripe credentials are not configured.
        /// </summary>
        Task<BalanceSummary?> SummarizeBalanceTransactionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    }

    public record BalanceSummary(int Count, long GrossCents, long FeeCents, long NetCents);

    public record PaymentIntentCreated(string IntentId, string ClientSecret);

    public record PaymentWebhookEvent(
        string Type,
        string? PaymentIntentId,
        string? PaymentIntentStatus,
        DisputeInfo? Dispute = null);

    public record DisputeInfo(
        string DisputeId,
        string PaymentIntentId,
        string? ChargeId,
        long AmountCents,
        string Currency,
        string? Reason,
        string Status,
        DateTime? EvidenceDueBy,
        DateTime StripeCreatedAt);

    public record RefundResult(string RefundId, string Status);
}
