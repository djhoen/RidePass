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

        /// <summary>
        /// Pushes funds from the platform balance to a connected (Express) account. The connected
        /// account's Stripe-managed payout schedule then deposits to the tenant's bank.
        /// </summary>
        Task<TransferResult> CreateTransferAsync(string connectAccountId, long amountCents, string currency,
            string? description = null, IReadOnlyDictionary<string, string>? metadata = null,
            string? idempotencyKey = null,
            CancellationToken ct = default);

        PaymentWebhookEvent? VerifyAndParseWebhook(string rawBody, string signatureHeader);

        Task<RefundResult> RefundAsync(string paymentIntentId, long? amountCents = null,
            string? idempotencyKey = null, CancellationToken ct = default);

        // ── Stripe Connect onboarding ────────────────────────────────────────────
        /// <summary>
        /// Creates a new Stripe Connect Standard account for a tenant. Returns the new
        /// Stripe account id (acct_xxx). The tenant must complete Stripe-hosted
        /// onboarding (via an Account Link) before they can charge.
        /// </summary>
        Task<string> CreateConnectAccountAsync(string tenantEmail, string tenantDisplayName, CancellationToken ct = default);

        /// <summary>
        /// Generates a Stripe-hosted onboarding URL for an existing Connect account.
        /// Stripe redirects the tenant admin back to <paramref name="returnUrl"/> when
        /// onboarding is complete (or to <paramref name="refreshUrl"/> if the link expires).
        /// </summary>
        Task<string> CreateAccountLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct = default);

        /// <summary>
        /// Reads a connected account's current status. Returns one of pending / active / restricted.
        /// </summary>
        Task<string> GetConnectAccountStatusAsync(string accountId, CancellationToken ct = default);

        /// <summary>
        /// Round-trips a no-op call to Stripe acting on the connected account, to prove the
        /// platform→connected access still works (the tenant could have revoked permissions
        /// from their Stripe Dashboard at any time). Returns a snapshot of capability/balance.
        /// Throws if the call fails.
        /// </summary>
        Task<ConnectTestResult> TestConnectAccountAsync(string accountId, CancellationToken ct = default);

        /// <summary>
        /// Returns the actual processor fee (in cents) Stripe charged for the latest charge on this
        /// PaymentIntent. Returns null if the PI is not yet captured / settled, or if Stripe credentials
        /// are not configured.
        /// </summary>
        Task<int?> GetActualStripeFeeCentsAsync(string paymentIntentId, CancellationToken ct = default);

        /// <summary>
        /// Reads a PaymentIntent's current status string at Stripe ("succeeded", "canceled",
        /// "requires_payment_method", "processing", etc.). Returns null when Stripe credentials
        /// aren't configured or the PI can't be fetched. Used by the pending-purchase reconciler
        /// to decide whether a stale pending purchase actually paid (finalize) or was abandoned (fail).
        /// </summary>
        Task<string?> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken ct = default);

        /// <summary>
        /// Cancels a PaymentIntent so it can no longer be charged, returning its resulting status
        /// ("canceled"). If Stripe rejects the cancel because the PI already reached a terminal
        /// state, returns that actual status instead (most importantly "succeeded", meaning the
        /// buyer completed payment in the race window) so the caller can finalize rather than fail.
        /// Returns null when Stripe isn't configured or the call fails unexpectedly. Used by the
        /// reconciler to make an abandoned PI permanently unchargeable before failing its rows.
        /// </summary>
        Task<string?> CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct = default);

        /// <summary>
        /// Sums Stripe balance_transactions in [fromUtc, toUtc). Used by the reconciliation view to
        /// compare what Stripe actually credited/debited against what we recorded in our ledger.
        /// Returns null when Stripe credentials are not configured.
        /// </summary>
        Task<BalanceSummary?> SummarizeBalanceTransactionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

        // ── Stripe Terminal (tap-to-pay on iPhone / Android) ─────────────────────
        /// <summary>
        /// Mints a short-lived Stripe Terminal connection token. The Stripe Terminal
        /// SDK (in the RidePassCashier mobile app) uses this to authenticate when
        /// discovering and connecting to readers — including the host phone itself
        /// when acting as a Tap to Pay reader. Tokens last ~10 minutes and the SDK
        /// asks for a new one when it expires.
        /// </summary>
        Task<string> CreateTerminalConnectionTokenAsync(string? locationId = null, CancellationToken ct = default);

        /// <summary>
        /// Creates a Stripe Terminal Location representing a physical site where
        /// readers exist. PaymentIntents created with payment_method_types=
        /// 'card_present' must reference a Location so the Stripe dashboard can
        /// group card-present sales by site. Lazily called once per tenant.
        /// </summary>
        Task<string> CreateTerminalLocationAsync(string displayName, TerminalLocationAddress address,
            CancellationToken ct = default);

        /// <summary>
        /// Creates a card-present PaymentIntent for the Stripe Terminal SDK to
        /// collect + confirm. Unlike the online PI flow, the SDK handles the
        /// payment method collection on-device; the backend just provisions the
        /// PI and (later) reads its succeeded status via the webhook.
        /// </summary>
        Task<PaymentIntentCreated> CreateCardPresentPaymentIntentAsync(
            long amountCents,
            string currency,
            string locationId,
            IReadOnlyDictionary<string, string> metadata,
            string? receiptEmail = null,
            CancellationToken ct = default);
    }

    public record TerminalLocationAddress(
        string Line1,
        string City,
        string Country,           // ISO 3166-1 alpha-2 (e.g., "US")
        string PostalCode,
        string? State = null);

    public record BalanceSummary(int Count, long GrossCents, long FeeCents, long NetCents);

    public record PaymentIntentCreated(string IntentId, string ClientSecret);

    public record PaymentWebhookEvent(
        string Type,
        string? PaymentIntentId,
        string? PaymentIntentStatus,
        DisputeInfo? Dispute = null,
        string? ConnectedAccountId = null,
        AccountInfo? Account = null,
        TransferEventInfo? Transfer = null);

    public record TransferEventInfo(
        string TransferId,
        string Destination,
        long AmountCents,
        string Currency,
        bool Reversed);

    public record AccountInfo(
        string AccountId,
        bool ChargesEnabled,
        bool PayoutsEnabled,
        bool DetailsSubmitted);

    public record ConnectTestResult(
        string AccountId,
        bool ChargesEnabled,
        bool PayoutsEnabled,
        long AvailableCents,
        long PendingCents,
        string Currency);

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

    public record TransferResult(string TransferId, string? BalanceTransactionId, long AmountCents, string Currency);
}
