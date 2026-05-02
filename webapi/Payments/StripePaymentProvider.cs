using Services.Payments;
using Stripe;

namespace webapi.Payments
{
    public class StripePaymentProvider : IPaymentProvider
    {
        private readonly string _secretKey;
        private readonly string _webhookSecret;
        private readonly ILogger<StripePaymentProvider> _logger;

        public StripePaymentProvider(IConfiguration configuration, ILogger<StripePaymentProvider> logger)
        {
            _secretKey = configuration["Stripe:SecretKey"] ?? string.Empty;
            _webhookSecret = configuration["Stripe:WebhookSecret"] ?? string.Empty;
            _logger = logger;

            if (!string.IsNullOrEmpty(_secretKey))
            {
                StripeConfiguration.ApiKey = _secretKey;
            }
        }

        public async Task<PaymentIntentCreated> CreatePaymentIntentAsync(
            long amountCents,
            string currency,
            IReadOnlyDictionary<string, string> metadata,
            string? receiptEmail = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey))
            {
                throw new InvalidOperationException(
                    "Stripe:SecretKey is not configured. Set it via user-secrets or environment variables before taking payments.");
            }

            var options = new PaymentIntentCreateOptions
            {
                Amount = amountCents,
                Currency = currency,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
                Metadata = metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
                ReceiptEmail = receiptEmail,
            };

            var service = new PaymentIntentService();
            var intent = await service.CreateAsync(options, cancellationToken: ct);
            return new PaymentIntentCreated(intent.Id, intent.ClientSecret);
        }

        public async Task<BalanceSummary?> SummarizeBalanceTransactionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey)) return null;
            try
            {
                var service = new BalanceTransactionService();
                var options = new BalanceTransactionListOptions
                {
                    Created = new DateRangeOptions
                    {
                        GreaterThanOrEqual = fromUtc,
                        LessThan = toUtc,
                    },
                    Limit = 100,
                };
                int count = 0;
                long gross = 0, fee = 0, net = 0;
                await foreach (var tx in service.ListAutoPagingAsync(options, cancellationToken: ct))
                {
                    count++;
                    gross += tx.Amount;
                    fee += tx.Fee;
                    net += tx.Net;
                }
                return new BalanceSummary(count, gross, fee, net);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Failed to summarize Stripe balance transactions for {From}-{To}", fromUtc, toUtc);
                return null;
            }
        }

        public async Task<int?> GetActualStripeFeeCentsAsync(string paymentIntentId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey)) return null;
            try
            {
                var service = new PaymentIntentService();
                var intent = await service.GetAsync(paymentIntentId, new PaymentIntentGetOptions
                {
                    Expand = new List<string> { "latest_charge.balance_transaction" }
                }, cancellationToken: ct);
                var fee = intent.LatestCharge?.BalanceTransaction?.Fee;
                if (fee is null) return null;
                if (fee.Value > int.MaxValue) return int.MaxValue;
                return (int)fee.Value;
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Failed to fetch Stripe fee for PaymentIntent {IntentId}.", paymentIntentId);
                return null;
            }
        }

        public async Task<RefundResult> RefundAsync(string paymentIntentId, long? amountCents = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey))
            {
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");
            }

            var options = new RefundCreateOptions { PaymentIntent = paymentIntentId };
            if (amountCents.HasValue) options.Amount = amountCents.Value;

            var service = new RefundService();
            var refund = await service.CreateAsync(options, cancellationToken: ct);
            return new RefundResult(refund.Id, refund.Status);
        }

        public PaymentWebhookEvent? VerifyAndParseWebhook(string rawBody, string signatureHeader)
        {
            if (string.IsNullOrEmpty(_webhookSecret))
            {
                _logger.LogError("Stripe:WebhookSecret is not configured; rejecting incoming webhook.");
                return null;
            }

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(rawBody, signatureHeader, _webhookSecret);
                string? intentId = null;
                string? intentStatus = null;
                DisputeInfo? disputeInfo = null;

                if (stripeEvent.Data.Object is PaymentIntent pi)
                {
                    intentId = pi.Id;
                    intentStatus = pi.Status;
                }
                else if (stripeEvent.Data.Object is Dispute d)
                {
                    disputeInfo = new DisputeInfo(
                        DisputeId: d.Id,
                        PaymentIntentId: d.PaymentIntentId ?? string.Empty,
                        ChargeId: d.ChargeId,
                        AmountCents: d.Amount,
                        Currency: d.Currency ?? "usd",
                        Reason: d.Reason,
                        Status: d.Status,
                        EvidenceDueBy: d.EvidenceDetails?.DueBy,
                        StripeCreatedAt: d.Created);
                    intentId = d.PaymentIntentId;
                }

                return new PaymentWebhookEvent(stripeEvent.Type, intentId, intentStatus, disputeInfo);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
                return null;
            }
        }
    }
}
