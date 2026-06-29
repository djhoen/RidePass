using Services.Payments;
using Stripe;

namespace webapi.Payments
{
    public class StripePaymentProvider : IPaymentProvider
    {
        private readonly string _secretKey;
        private readonly string _webhookSecret;
        private readonly string _connectWebhookSecret;
        private readonly ILogger<StripePaymentProvider> _logger;

        public StripePaymentProvider(IConfiguration configuration, ILogger<StripePaymentProvider> logger)
        {
            _secretKey = configuration["Stripe:SecretKey"] ?? string.Empty;
            _webhookSecret = configuration["Stripe:WebhookSecret"] ?? string.Empty;
            // Direct-charge (Connect) events arrive on the connected account and are signed by a
            // separate Connect webhook endpoint, so they need their own signing secret.
            _connectWebhookSecret = configuration["Stripe:ConnectWebhookSecret"] ?? string.Empty;
            _logger = logger;

            if (!string.IsNullOrEmpty(_secretKey))
            {
                StripeConfiguration.ApiKey = _secretKey;
            }
        }

        // Builds RequestOptions for acting on a connected account (direct charge) and/or with an
        // idempotency key. Returns null when neither is needed so platform-account calls are unchanged.
        private static RequestOptions? BuildRequestOptions(string? connectedAccountId, string? idempotencyKey = null)
        {
            if (string.IsNullOrEmpty(connectedAccountId) && string.IsNullOrEmpty(idempotencyKey)) return null;
            var options = new RequestOptions();
            if (!string.IsNullOrEmpty(connectedAccountId)) options.StripeAccount = connectedAccountId;
            if (!string.IsNullOrEmpty(idempotencyKey)) options.IdempotencyKey = idempotencyKey;
            return options;
        }

        public async Task<PaymentIntentCreated> CreatePaymentIntentAsync(
            long amountCents,
            string currency,
            IReadOnlyDictionary<string, string> metadata,
            string? receiptEmail = null,
            string? connectedAccountId = null,
            long? applicationFeeCents = null,
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
            // Direct charge: when a connected account is supplied we create the PI ON that account
            // (RequestOptions.StripeAccount) and route our service fee to the platform as the
            // application fee. That is how RidePass still gets paid while the tenant is MoR.
            if (applicationFeeCents is > 0)
            {
                options.ApplicationFeeAmount = applicationFeeCents.Value;
            }

            var service = new PaymentIntentService();
            var intent = await service.CreateAsync(options, BuildRequestOptions(connectedAccountId), ct);
            return new PaymentIntentCreated(intent.Id, intent.ClientSecret);
        }

        public async Task<TransferResult> CreateTransferAsync(string connectAccountId, long amountCents, string currency,
            string? description = null, IReadOnlyDictionary<string, string>? metadata = null,
            string? idempotencyKey = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey))
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");

            var options = new TransferCreateOptions
            {
                Amount = amountCents,
                Currency = currency,
                Destination = connectAccountId,
                Description = description,
                Metadata = metadata?.ToDictionary(kv => kv.Key, kv => kv.Value),
            };
            var service = new TransferService();
            // Idempotency key (the payout id) so a retry or a double admin-click can't
            // create a second real-money transfer for the same payout.
            var requestOptions = string.IsNullOrEmpty(idempotencyKey)
                ? null
                : new RequestOptions { IdempotencyKey = idempotencyKey };
            var transfer = await service.CreateAsync(options, requestOptions, ct);
            return new TransferResult(
                TransferId: transfer.Id,
                BalanceTransactionId: transfer.BalanceTransactionId,
                AmountCents: transfer.Amount,
                Currency: transfer.Currency.ToUpperInvariant());
        }

        public async Task<string> CreateConnectAccountAsync(string tenantEmail, string tenantDisplayName, string accountType = "express", CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey))
            {
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");
            }
            var service = new Stripe.AccountService();
            var options = new Stripe.AccountCreateOptions
            {
                Type = accountType,
                Email = tenantEmail,
                BusinessProfile = new Stripe.AccountBusinessProfileOptions { Name = tenantDisplayName },
                Metadata = new Dictionary<string, string> { ["ridepass_tenant"] = tenantDisplayName },
            };
            if (accountType == "standard")
            {
                // Standard: the tenant owns the Stripe relationship/dashboard and is merchant of
                // record on direct charges. They manage their own capabilities, so we request
                // card_payments (needed to accept the direct charges) and let Stripe handle the rest.
                options.Capabilities = new Stripe.AccountCapabilitiesOptions
                {
                    CardPayments = new Stripe.AccountCapabilitiesCardPaymentsOptions { Requested = true },
                };
            }
            else
            {
                // Express: Stripe-hosted onboarding (bank info + minimal KYC), platform owns the
                // relationship. Charges run on the platform account; we move funds to the connected
                // account via Transfer.create when paying out (the 'platform' charge-mode payout rail).
                options.Capabilities = new Stripe.AccountCapabilitiesOptions
                {
                    Transfers = new Stripe.AccountCapabilitiesTransfersOptions { Requested = true },
                };
            }
            var account = await service.CreateAsync(options, cancellationToken: ct);
            return account.Id;
        }

        public async Task<string> CreateAccountLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey))
            {
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");
            }
            var service = new Stripe.AccountLinkService();
            var options = new Stripe.AccountLinkCreateOptions
            {
                Account = accountId,
                Type = "account_onboarding",
                ReturnUrl = returnUrl,
                RefreshUrl = refreshUrl,
            };
            var link = await service.CreateAsync(options, cancellationToken: ct);
            return link.Url;
        }

        public async Task<string> GetConnectAccountStatusAsync(string accountId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey)) return "pending";
            var service = new Stripe.AccountService();
            var account = await service.GetAsync(accountId, cancellationToken: ct);
            // ChargesEnabled + PayoutsEnabled together mean the account is fully set up.
            // DetailsSubmitted=false means they haven't finished the onboarding link yet.
            if (!account.DetailsSubmitted) return "pending";
            if (account.ChargesEnabled && account.PayoutsEnabled) return "active";
            return "restricted";
        }

        public async Task<ConnectTestResult> TestConnectAccountAsync(string accountId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey))
                throw new InvalidOperationException("Stripe is not configured.");

            // Two calls: GetAsync verifies the account exists and we have platform access; the
            // BalanceService call with StripeAccount header proves we can act on the connected
            // account's behalf. If the tenant revoked our access in their Dashboard, the second
            // call throws PermissionError ("...does not have access to account...").
            var accountService = new Stripe.AccountService();
            var account = await accountService.GetAsync(accountId, cancellationToken: ct);

            var balanceService = new BalanceService();
            var balance = await balanceService.GetAsync(
                requestOptions: new RequestOptions { StripeAccount = accountId },
                cancellationToken: ct);

            var available = balance.Available?.FirstOrDefault();
            var pending = balance.Pending?.FirstOrDefault();
            return new ConnectTestResult(
                AccountId: account.Id,
                ChargesEnabled: account.ChargesEnabled,
                PayoutsEnabled: account.PayoutsEnabled,
                AvailableCents: available?.Amount ?? 0,
                PendingCents: pending?.Amount ?? 0,
                Currency: (available?.Currency ?? pending?.Currency ?? "usd").ToUpperInvariant());
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

        public async Task<int?> GetActualStripeFeeCentsAsync(string paymentIntentId, string? connectedAccountId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey)) return null;
            try
            {
                var service = new PaymentIntentService();
                var intent = await service.GetAsync(paymentIntentId, new PaymentIntentGetOptions
                {
                    Expand = new List<string> { "latest_charge.balance_transaction" }
                }, BuildRequestOptions(connectedAccountId), ct);
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

        public async Task<string?> GetPaymentIntentStatusAsync(string paymentIntentId, string? connectedAccountId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey)) return null;
            try
            {
                var service = new PaymentIntentService();
                var intent = await service.GetAsync(paymentIntentId, requestOptions: BuildRequestOptions(connectedAccountId), cancellationToken: ct);
                return intent.Status;
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Failed to fetch Stripe status for PaymentIntent {IntentId}.", paymentIntentId);
                return null;
            }
        }

        public async Task<string?> CancelPaymentIntentAsync(string paymentIntentId, string? connectedAccountId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey)) return null;
            var service = new PaymentIntentService();
            var requestOptions = BuildRequestOptions(connectedAccountId);
            try
            {
                var canceled = await service.CancelAsync(paymentIntentId, requestOptions: requestOptions, cancellationToken: ct);
                return canceled.Status;
            }
            catch (StripeException ex)
            {
                // Cancel is rejected once the PI reaches a terminal state — most importantly
                // 'succeeded', meaning the buyer completed payment in the window. Re-read the
                // real status so the caller can finalize instead of failing the rows.
                _logger.LogWarning(ex, "Cancel rejected for PaymentIntent {IntentId}; re-reading status.", paymentIntentId);
                try
                {
                    var current = await service.GetAsync(paymentIntentId, requestOptions: requestOptions, cancellationToken: ct);
                    return current.Status;
                }
                catch (StripeException ex2)
                {
                    _logger.LogWarning(ex2, "Failed to re-read PaymentIntent {IntentId} after cancel.", paymentIntentId);
                    return null;
                }
            }
        }

        // ── Stripe Terminal (tap-to-pay) ─────────────────────────────────────
        public async Task<string> CreateTerminalConnectionTokenAsync(string? locationId = null, string? connectedAccountId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey))
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");

            // Scoping the token to a Location restricts which readers it can
            // discover/connect to — the SDK rejects mismatched locations. Pass
            // null only for first-run discovery before the tenant has a Location.
            var service = new Stripe.Terminal.ConnectionTokenService();
            var options = new Stripe.Terminal.ConnectionTokenCreateOptions();
            if (!string.IsNullOrWhiteSpace(locationId)) options.Location = locationId;
            var token = await service.CreateAsync(options, BuildRequestOptions(connectedAccountId), ct);
            return token.Secret;
        }

        // Stripe requires an ISO 3166-1 alpha-2 country code (e.g. "US"), but a tenant's stored country can
        // be a 3-letter code or full name ("USA", "United States"). Map the common cases; pass anything
        // already 2-letter (or unknown) through uppercased and let Stripe validate.
        private static string? NormalizeCountry(string? country)
        {
            if (string.IsNullOrWhiteSpace(country)) return country;
            var c = country.Trim();
            if (c.Length == 2) return c.ToUpperInvariant();
            return c.ToUpperInvariant() switch
            {
                "USA" or "UNITED STATES" or "UNITED STATES OF AMERICA" or "U.S." or "U.S.A." => "US",
                "CAN" or "CANADA" => "CA",
                "MEX" or "MEXICO" => "MX",
                "GBR" or "UK" or "UNITED KINGDOM" or "GREAT BRITAIN" => "GB",
                "AUS" or "AUSTRALIA" => "AU",
                "NZL" or "NEW ZEALAND" => "NZ",
                _ => c.ToUpperInvariant(),
            };
        }

        public async Task<string> CreateTerminalLocationAsync(string displayName, TerminalLocationAddress address,
            string? connectedAccountId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey))
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");

            var service = new Stripe.Terminal.LocationService();
            var options = new Stripe.Terminal.LocationCreateOptions
            {
                DisplayName = displayName,
                Address = new Stripe.AddressOptions
                {
                    Line1 = address.Line1,
                    City = address.City,
                    State = address.State,
                    Country = NormalizeCountry(address.Country),
                    PostalCode = address.PostalCode,
                },
            };
            // Direct charge: the Location must belong to the connected account the card-present
            // PaymentIntents will be created on.
            var loc = await service.CreateAsync(options, BuildRequestOptions(connectedAccountId), ct);
            return loc.Id;
        }

        public async Task<PaymentIntentCreated> CreateCardPresentPaymentIntentAsync(
            long amountCents,
            string currency,
            string locationId,
            IReadOnlyDictionary<string, string> metadata,
            string? receiptEmail = null,
            string? connectedAccountId = null,
            long? applicationFeeCents = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey))
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");

            // card_present + a Location is the canonical shape for Tap to Pay /
            // Terminal readers. CaptureMethod=Automatic means Stripe captures as
            // soon as the SDK confirms; the alternative (Manual) is for auth+
            // settle later flows we don't need.
            var options = new PaymentIntentCreateOptions
            {
                Amount = amountCents,
                Currency = currency,
                PaymentMethodTypes = new List<string> { "card_present" },
                CaptureMethod = "automatic",
                Metadata = metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
                ReceiptEmail = receiptEmail,
            };
            // Stripe Terminal requires the destination Location stamped via the
            // payment_method_options.card_present configuration on the PI for
            // dashboard grouping; the SDK also validates locally.
            options.PaymentMethodOptions = new PaymentIntentPaymentMethodOptionsOptions
            {
                CardPresent = new PaymentIntentPaymentMethodOptionsCardPresentOptions(),
            };
            options.Metadata["stripe_terminal_location_id"] = locationId;
            // Direct charge on the tenant's own account: route our service fee as the application fee.
            if (applicationFeeCents is > 0)
            {
                options.ApplicationFeeAmount = applicationFeeCents.Value;
            }

            var service = new PaymentIntentService();
            var intent = await service.CreateAsync(options, BuildRequestOptions(connectedAccountId), ct);
            return new PaymentIntentCreated(intent.Id, intent.ClientSecret);
        }

        public async Task<RefundResult> RefundAsync(string paymentIntentId, long? amountCents = null,
            string? idempotencyKey = null, string? connectedAccountId = null,
            bool refundApplicationFee = false, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_secretKey))
            {
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");
            }

            var options = new RefundCreateOptions { PaymentIntent = paymentIntentId };
            if (amountCents.HasValue) options.Amount = amountCents.Value;
            // For a direct charge the refund is issued on the connected account; returning the
            // application fee hands the tenant back our cut on the refunded portion too.
            if (refundApplicationFee) options.RefundApplicationFee = true;

            var service = new RefundService();
            // Idempotency key (the purchase id) so a retry or a double-click can't issue
            // a second refund for the same purchase; StripeAccount targets the connected account.
            var refund = await service.CreateAsync(options, BuildRequestOptions(connectedAccountId, idempotencyKey), ct);
            return new RefundResult(refund.Id, refund.Status);
        }

        public PaymentWebhookEvent? VerifyAndParseWebhook(string rawBody, string signatureHeader, bool connect = false)
        {
            var secret = connect ? _connectWebhookSecret : _webhookSecret;
            if (string.IsNullOrEmpty(secret))
            {
                _logger.LogError("Stripe:{SecretName} is not configured; rejecting incoming webhook.",
                    connect ? "ConnectWebhookSecret" : "WebhookSecret");
                return null;
            }

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(rawBody, signatureHeader, secret);
                string? intentId = null;
                string? intentStatus = null;
                DisputeInfo? disputeInfo = null;
                AccountInfo? accountInfo = null;
                TransferEventInfo? transferInfo = null;

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
                else if (stripeEvent.Data.Object is Stripe.Account acct)
                {
                    accountInfo = new AccountInfo(
                        AccountId: acct.Id,
                        ChargesEnabled: acct.ChargesEnabled,
                        PayoutsEnabled: acct.PayoutsEnabled,
                        DetailsSubmitted: acct.DetailsSubmitted);
                }
                else if (stripeEvent.Data.Object is Stripe.Transfer tr)
                {
                    transferInfo = new TransferEventInfo(
                        TransferId: tr.Id,
                        Destination: tr.DestinationId ?? string.Empty,
                        AmountCents: tr.Amount,
                        Currency: tr.Currency ?? "usd",
                        Reversed: tr.Reversed);
                }

                return new PaymentWebhookEvent(stripeEvent.Type, intentId, intentStatus, disputeInfo, stripeEvent.Account, accountInfo, transferInfo);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe webhook signature verification failed.");
                return null;
            }
        }
    }
}
