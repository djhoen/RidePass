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
    }

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
