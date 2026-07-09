using Services.Helpers;
using Services.Notifications;
using Services.Payments;
using Services.Repositories.Data.ExtrasData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.RentalData;
using Services.Repositories.Interfaces;
using Services.Rewards;

namespace webapi.Payments
{
    /// <summary>
    /// Single source of truth for PaymentIntent-keyed purchase fulfillment. Shared by
    /// the live Stripe webhook (PaymentController) and the pending-purchase reconciler.
    /// Behavior is identical to the original PaymentController.StripeWebhook body: same
    /// status guards, ordering, idempotency catches, log messages, and email bodies.
    /// </summary>
    public class StripePurchaseFinalizer : IStripePurchaseFinalizer
    {
        private readonly IPaymentProvider _payments;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IFeeCalculator _feeCalculator;
        private readonly ITenantLedgerRepository _ledger;
        private readonly INotificationService _notifications;
        private readonly IRewardEngine _rewardEngine;
        private readonly IRewardRepository _rewards;
        private readonly IUserRepository _users;
        private readonly ISeasonPassRepository _seasonPasses;
        private readonly ITenantRepository _tenants;
        private readonly IEventTicketTierRepository _tiers;
        private readonly Services.Coupons.IBundledCouponMinter _bundledCouponMinter;
        private readonly IGiftCardRepository _giftCards;
        private readonly ICouponRepository _coupons;
        private readonly Services.GiftCards.IGiftCardDeliveryService _giftCardDelivery;
        private readonly IRentalRepository _rentals;
        private readonly IEventWaitlistRepository _waitlist;
        private readonly IEventExtraRepository _extras;
        private readonly IMembershipRepository _memberships;
        private readonly IConcessionRepository _concessions;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ISmtpEmailer _emailer;
        private readonly IConfiguration _config;
        private readonly ILogger<StripePurchaseFinalizer> _logger;
        private readonly int _largeSaleThresholdCents;

        public StripePurchaseFinalizer(
            IPaymentProvider payments,
            IEventTicketPurchaseRepository ticketPurchases,
            IFeeCalculator feeCalculator,
            ITenantLedgerRepository ledger,
            INotificationService notifications,
            IRewardEngine rewardEngine,
            IRewardRepository rewards,
            IUserRepository users,
            ISeasonPassRepository seasonPasses,
            ITenantRepository tenants,
            IEventTicketTierRepository tiers,
            Services.Coupons.IBundledCouponMinter bundledCouponMinter,
            IGiftCardRepository giftCards,
            ICouponRepository coupons,
            Services.GiftCards.IGiftCardDeliveryService giftCardDelivery,
            IRentalRepository rentals,
            IEventWaitlistRepository waitlist,
            IEventExtraRepository extras,
            IMembershipRepository memberships,
            IConcessionRepository concessions,
            IEmailSuppressionRepository suppression,
            ISmtpEmailer emailer,
            IConfiguration configuration,
            ILogger<StripePurchaseFinalizer> logger)
        {
            _payments = payments;
            _ticketPurchases = ticketPurchases;
            _feeCalculator = feeCalculator;
            _ledger = ledger;
            _notifications = notifications;
            _rewardEngine = rewardEngine;
            _rewards = rewards;
            _users = users;
            _seasonPasses = seasonPasses;
            _tenants = tenants;
            _tiers = tiers;
            _bundledCouponMinter = bundledCouponMinter;
            _giftCards = giftCards;
            _coupons = coupons;
            _giftCardDelivery = giftCardDelivery;
            _rentals = rentals;
            _waitlist = waitlist;
            _extras = extras;
            _memberships = memberships;
            _concessions = concessions;
            _suppression = suppression;
            _emailer = emailer;
            _config = configuration;
            _logger = logger;
            _largeSaleThresholdCents = configuration.GetValue<int?>("Notifications:LargeSaleThresholdCents") ?? 50_000;  // $500 default
        }

        public async Task ProcessPaymentIntentAsync(string paymentIntentId, string eventType, CancellationToken ct = default)
        {
            // A counter sale can attach multiple purchase rows (mixed kinds) to one PaymentIntent,
            // so iterate everything that points at this PI rather than stopping after the first match.
            var tickets = await _ticketPurchases.ListByStripePaymentIntentId(paymentIntentId);
            var seasonPass = await _seasonPasses.GetPurchaseByStripePaymentIntentId(paymentIntentId);
            var giftCard = await _giftCards.GetByPaymentIntentId(paymentIntentId);
            var rental = await _rentals.GetPurchaseByRentalPaymentIntentId(paymentIntentId);
            var waitlistPrepay = await _waitlist.GetByPrepayPaymentIntentId(paymentIntentId);
            var extras = await _extras.ListByPaymentIntentId(paymentIntentId);
            var membership = await _memberships.GetByPaymentIntentId(paymentIntentId);
            var concessionSale = await _concessions.GetSaleByPaymentIntentId(paymentIntentId);

            if (tickets.Count == 0 && seasonPass is null && giftCard is null && rental is null && waitlistPrepay is null && extras.Count == 0 && membership is null && concessionSale is null)
            {
                _logger.LogWarning("Received Stripe event {EventType} for unknown payment_intent {IntentId}",
                    eventType, paymentIntentId);
                return;
            }

            // Direct charge detection: in 'direct' mode the charge ran on the tenant's own connected
            // account, snapshotted on the purchase row at charge time. The ticket rows carry the
            // snapshot; anything bundled on the same PaymentIntent (extras / membership) shares the
            // charge, so one flag drives the ledger semantics for the whole PI. For a direct charge
            // the tenant is MoR (they hold the funds and bore the Stripe fee), so we record no Stripe
            // fee and no net-to-tenant, and our cut = the application fee Stripe routed to us.
            var connectedAccountId = tickets
                    .FirstOrDefault(t => !string.IsNullOrEmpty(t.StripeConnectedAccountId))?.StripeConnectedAccountId
                ?? extras.FirstOrDefault(e => !string.IsNullOrEmpty(e.StripeConnectedAccountId))?.StripeConnectedAccountId
                ?? membership?.StripeConnectedAccountId
                ?? seasonPass?.StripeConnectedAccountId
                ?? rental?.StripeConnectedAccountId;
            var isDirect = !string.IsNullOrEmpty(connectedAccountId);

            // Gift-card purchase: the card is minted 'pending' (not spendable/deliverable). On
            // success we activate it, then immediate-delivery cards get emailed inline while
            // future-scheduled cards stay 'pending' delivery for the worker (which gates on
            // status='active'). On failure we void it so a declined/abandoned purchase can never
            // produce a live card.
            if (giftCard is not null)
            {
                if (eventType == "payment_intent.succeeded")
                {
                    // Only the call that actually activates the card sends the delivery email, so a late
                    // webhook racing the reconciler (both processing 'succeeded') can't email twice.
                    var justActivated = await _giftCards.Activate(giftCard.Id);
                    giftCard.Status = "active";
                    if (justActivated
                        && (!giftCard.ScheduledDeliveryAtUtc.HasValue || giftCard.ScheduledDeliveryAtUtc.Value <= DateTime.UtcNow))
                    {
                        _ = _giftCardDelivery.SendDeliveryEmail(giftCard);
                    }
                }
                else if (eventType == "payment_intent.payment_failed")
                {
                    await _giftCards.Void(giftCard.Id);
                }
                return;
            }

            // Waitlist pre-pay: flip is_prepaid=true on success. Failure surfaces
            // as a normal Stripe failure; the row stays in 'waiting' (just not
            // pre-paid), so promotion goes through the regular pay-when-promoted
            // flow.
            if (waitlistPrepay is not null)
            {
                if (eventType == "payment_intent.succeeded")
                {
                    await _waitlist.MarkPrepaid(waitlistPrepay.Id, waitlistPrepay.PrepayAmountCents);
                }
                return;
            }

            // Membership purchase: flip pending → paid + write a sale ledger entry.
            // Counter sales can bundle a membership with passes / tickets / extras on the
            // same PI, so we don't early-return when other kinds also matched.
            if (membership is not null)
            {
                if (eventType == "payment_intent.succeeded" && membership.Status == "pending")
                {
                    // A bundled membership (addMembership) rides on the same PI as the passes /
                    // tickets, which absorb the Stripe fee in OnPaymentSucceeded. So the
                    // membership only carries the fee when it's standalone on its own PI;
                    // otherwise it would double-count the PI fee in the ledger.
                    await OnMembershipPaid(membership, membershipOwnsTheFee: tickets.Count == 0, isDirect);
                }
                else if (eventType == "payment_intent.payment_failed" && membership.Status == "pending")
                {
                    await _memberships.UpdateStatus(membership.Id, "failed");
                }
                if (tickets.Count == 0 && seasonPass is null && extras.Count == 0)
                {
                    return;
                }
            }

            // Event extras (camping/parking/pit-vehicle/etc.): flip every line in the
            // cart that pointed at this PI to 'paid' or 'failed' as a group. Extras can
            // ride along with a pass purchase (Reserve a Spot upsell), so don't early-
            // return — fall through and let the pass/ticket switch run too.
            if (extras.Count > 0)
            {
                if (eventType == "payment_intent.succeeded")
                {
                    // When other gross-bearing kinds share this PaymentIntent they absorb the
                    // Stripe fee in their own ledger rows, so extras carry zero fee then (the PI
                    // fee is counted exactly once). When extras are alone on the PI (the spectator
                    // Gate Fee flow), they get the whole fee distributed across them.
                    var extrasOwnTheFee = tickets.Count == 0
                        && membership is null && seasonPass is null;
                    await OnExtrasPaid(paymentIntentId, extras, extrasOwnTheFee, isDirect);
                }
                else if (eventType == "payment_intent.payment_failed")
                {
                    foreach (var x in extras.Where(e => e.Status == "pending"))
                        await _extras.UpdateStatus(x.Id, "failed");
                }
                if (tickets.Count == 0 && seasonPass is null)
                {
                    return;
                }
            }

            // Rental: flip pending → paid (reservation now firmly holds capacity).
            if (rental is not null)
            {
                if (eventType == "payment_intent.succeeded" && rental.Status == "pending")
                {
                    await OnRentalPaid(rental, isDirect);
                }
                else if (eventType == "payment_intent.payment_failed" && rental.Status == "pending")
                {
                    await _rentals.UpdateStatus(rental.Id, "failed");
                    await RestoreDiscountsFor("rental", new[] { rental.Id });
                }
                return;
            }

            // Concession sale (cashier tap-to-pay, anonymous buyer): always standalone on its
            // own PaymentIntent, so flip pending -> paid + write the ledger entry, then return.
            if (concessionSale is not null)
            {
                if (eventType == "payment_intent.succeeded" && concessionSale.Status == "pending")
                {
                    await OnConcessionPaid(concessionSale);
                }
                else if (eventType == "payment_intent.payment_failed" && concessionSale.Status == "pending")
                {
                    await _concessions.MarkSaleFailed(concessionSale.Id);
                }
                return;
            }

            switch (eventType)
            {
                case "payment_intent.succeeded":
                    await OnPaymentSucceeded(paymentIntentId, tickets, isDirect);
                    if (seasonPass is not null && seasonPass.Status == "pending")
                    {
                        await OnSeasonPassPaid(seasonPass, isDirect);
                    }
                    break;
                case "payment_intent.payment_failed":
                    var failedTickets = tickets.Where(p => p.Status == "pending").ToList();
                    foreach (var t in failedTickets)
                        await _ticketPurchases.UpdateStatus(t.Id, "failed");
                    if (failedTickets.Count > 0)
                        await RestoreDiscountsFor("event_ticket", failedTickets.Select(t => t.Id).ToList());
                    if (seasonPass is not null && seasonPass.Status == "pending")
                        await _seasonPasses.UpdatePurchaseStatus(seasonPass.Id, "failed");
                    break;
            }
        }

        // Checkout debits gift-card balance and writes gift-card + coupon redemption rows at
        // PaymentIntent-creation time (BuyEventTicket for tickets, RentalController.Buy for rentals).
        // When that cart fails or is abandoned we must hand the gift-card balance back and free the
        // coupon usage, otherwise a declined card permanently consumes the rider's gift card and
        // burns a limited-use coupon. Keyed on the just-failed source rows. The gift-card delete uses
        // RETURNING so only the call that actually removes the rows restores their amount, making this
        // safe under a duplicate/racing finalizer pass; the coupon delete is naturally idempotent.
        private async Task RestoreDiscountsFor(string sourceKind, IReadOnlyList<Guid> sourceIds)
        {
            if (sourceIds.Count == 0) return;

            var removedGiftCard = await _giftCards.DeleteRedemptionsBySource(sourceKind, sourceIds);
            foreach (var byCard in removedGiftCard.GroupBy(r => r.GiftCardId))
            {
                var restore = byCard.Sum(r => r.AmountCents);
                if (restore > 0) await _giftCards.RestoreBalance(byCard.Key, restore);
            }

            await _coupons.DeleteRedemptionsBySource(sourceKind, sourceIds);
        }

        private async Task OnMembershipPaid(Services.Repositories.Data.MembershipData.MembershipPurchase m, bool membershipOwnsTheFee, bool isDirect)
        {
            // Direct charge: the tenant's account bore the Stripe fee, so we record none.
            // Otherwise zero fee when bundled with passes/tickets (they absorbed the PI fee) so the
            // PI's Stripe fee is counted exactly once across the ledger.
            var stripeFee = (isDirect || !membershipOwnsTheFee) ? 0 : (await _payments.GetActualStripeFeeCentsAsync(m.StripePaymentIntentId!) ?? 0);
            await _memberships.UpdateStatus(m.Id, "paid");
            try
            {
                var calc = await _feeCalculator.Calculate(m.TenantId, m.AmountCents, stripeFee, m.ServiceChargeCents, DateTime.UtcNow, isDirect);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = m.TenantId,
                    EntryKind = "sale",
                    SourceKind = "membership",
                    SourceId = m.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = m.AmountCents,
                    StripeFeeCents = stripeFee,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = calc.NetToTenantCents,
                    StripePaymentIntentId = m.StripePaymentIntentId,
                    PaymentMethod = isDirect ? "stripe_direct" : "stripe",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Ledger entry for membership {Id} already exists; skipping.", m.Id);
            }
        }

        private async Task OnSeasonPassPaid(SeasonPassPurchase pass, bool isDirect)
        {
            // Direct charge: the tenant's account bore the Stripe fee, so we record none.
            var stripeFee = isDirect ? 0 : (await _payments.GetActualStripeFeeCentsAsync(pass.StripePaymentIntentId!) ?? 0);
            await _seasonPasses.UpdatePurchaseStatus(pass.Id, "paid");
            try
            {
                var calc = await _feeCalculator.Calculate(pass.TenantId, pass.AmountCents, stripeFee, pass.ServiceChargeCents, DateTime.UtcNow, isDirect);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = pass.TenantId,
                    EntryKind = "sale",
                    SourceKind = "season_pass",
                    SourceId = pass.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = pass.AmountCents,
                    StripeFeeCents = stripeFee,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = calc.NetToTenantCents,
                    StripePaymentIntentId = pass.StripePaymentIntentId,
                    PaymentMethod = isDirect ? "stripe_direct" : "stripe",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Ledger entry for season_pass {Id} already exists; skipping.", pass.Id);
            }
            // Season passes are always bought by a logged-in rider (PurchaserUserId is non-nullable),
            // so there's no guest case here.
            _ = SendPurchaseEmailAsync(pass.TenantId, pass.PurchaserEmail, pass.PurchaserName, pass.RedemptionToken,
                "season_pass", pass.AmountCents, null, isGuest: false);
        }

        // Gate fees and other add-ons: flip pending rows to paid AND write a sale ledger
        // entry per row so the revenue reaches the tenant's balance/payout. Previously these
        // only flipped status and never hit the ledger, so spectator/add-on income was lost
        // from payouts. Idempotent: the unique (tenant, source_kind, source_id) sale index
        // makes a duplicate webhook/reconciler pass a no-op.
        private async Task OnExtrasPaid(string paymentIntentId, List<EventExtraPurchase> extras, bool extrasOwnTheFee, bool isDirect)
        {
            var pending = extras.Where(e => e.Status == "pending").ToList();
            if (pending.Count == 0) return;

            // Direct charge: the tenant's account bore the Stripe fee, so we record none.
            var totalFee = (isDirect || !extrasOwnTheFee) ? 0 : (await _payments.GetActualStripeFeeCentsAsync(paymentIntentId) ?? 0);
            var totalGross = pending.Sum(e => (long)e.AmountCents);
            var feeDistributed = 0;
            var occurredAt = DateTime.UtcNow;

            for (var i = 0; i < pending.Count; i++)
            {
                var x = pending[i];
                // Last line absorbs the rounding remainder so the per-line fees sum to totalFee.
                var stripeFeeForLine = (totalFee == 0 || totalGross == 0)
                    ? 0
                    : (i == pending.Count - 1 ? totalFee - feeDistributed : (int)(totalFee * x.AmountCents / totalGross));
                feeDistributed += stripeFeeForLine;

                await _extras.UpdateStatus(x.Id, "paid");
                x.Status = "paid";
                try
                {
                    var calc = await _feeCalculator.Calculate(x.TenantId, x.AmountCents, stripeFeeForLine, x.ServiceChargeCents, occurredAt, isDirect);
                    await _ledger.Insert(new TenantLedgerEntry
                    {
                        TenantId = x.TenantId,
                        EntryKind = "sale",
                        SourceKind = "extras",
                        SourceId = x.Id,
                        OccurredAtUtc = occurredAt,
                        GrossCents = x.AmountCents,
                        StripeFeeCents = stripeFeeForLine,
                        RidepassCutCents = calc.RidepassCutCents,
                        NetToTenantCents = calc.NetToTenantCents,
                        StripePaymentIntentId = paymentIntentId,
                        PaymentMethod = isDirect ? "stripe_direct" : "stripe",
                    });
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
                {
                    _logger.LogDebug("Ledger entry for extras {Id} already exists; skipping.", x.Id);
                }
            }
        }

        // Rental: flip to paid AND write a sale ledger entry. A rental is the only sale on
        // its PaymentIntent (the security deposit is a separate hold on its own PI), so it
        // carries the full Stripe fee.
        private async Task OnRentalPaid(RentalPurchase r, bool isDirect)
        {
            // Direct charge: the tenant's account bore the Stripe fee, so we record none.
            var stripeFee = isDirect ? 0 : (await _payments.GetActualStripeFeeCentsAsync(r.RentalPiId!) ?? 0);
            await _rentals.UpdateStatus(r.Id, "paid");
            try
            {
                var calc = await _feeCalculator.Calculate(r.TenantId, r.AmountCents, stripeFee, r.ServiceChargeCents, DateTime.UtcNow, isDirect);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = r.TenantId,
                    EntryKind = "sale",
                    SourceKind = "rental",
                    SourceId = r.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = r.AmountCents,
                    StripeFeeCents = stripeFee,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = calc.NetToTenantCents,
                    StripePaymentIntentId = r.RentalPiId,
                    PaymentMethod = isDirect ? "stripe_direct" : "stripe",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Ledger entry for rental {Id} already exists; skipping.", r.Id);
            }
        }

        // Concession sale: standalone on its own PI, so it owns the whole Stripe fee.
        // All-in pricing means no rider service charge (serviceChargeCents = 0); the RidePass
        // cut is computed from the tenant's bps on the gross.
        private async Task OnConcessionPaid(Services.Repositories.Data.ConcessionData.ConcessionSale sale)
        {
            // Direct mode = the card sale ran on the tenant's own connected account (snapshotted on
            // the sale). In direct mode the tenant bore the Stripe fee, so we record no Stripe fee and
            // no net-to-tenant (they already hold the funds).
            var isDirect = !string.IsNullOrEmpty(sale.StripeConnectedAccountId);
            var stripeFee = isDirect ? 0 : (await _payments.GetActualStripeFeeCentsAsync(sale.StripePaymentIntentId!) ?? 0);
            await _concessions.MarkSalePaid(sale.Id);
            // Assign the pickup number now that the card sale is paid (cash sales get theirs at the
            // counter). Skipped automatically on a duplicate webhook (order_number already set).
            if (sale.OrderNumber is null)
            {
                var orderNumber = await _concessions.NextOrderNumber(sale.TenantId);
                await _concessions.SetOrderNumber(sale.Id, orderNumber);
                // Deplete theoretical inventory once, when the order number is first assigned (so a
                // duplicate webhook can't double-deplete). Best-effort.
                try { await _concessions.DepleteInventoryForSale(sale.Id, sale.TenantId); } catch { /* inventory is best-effort */ }
                // Alert F&B managers + admins about any item that just went low (de-duped, best-effort).
                try
                {
                    var low = await _concessions.MarkAndGetNewlyLowStock(sale.TenantId);
                    if (low.Count > 0)
                    {
                        var names = string.Join(", ", low.Select(i => i.Name));
                        var title = low.Count == 1 ? "1 item low on stock" : $"{low.Count} items low on stock";
                        await _notifications.EmitToTenantRoles(sale.TenantId, new[] { "tenant_manager", "tenant_admin" },
                            NotificationKinds.LowStock, title, $"Running low: {names}.", "/Admin/Concessions");
                    }
                }
                catch { /* alerting is best-effort */ }
            }
            try
            {
                var calc = await _feeCalculator.Calculate(sale.TenantId, sale.TotalCents, stripeFee, 0, DateTime.UtcNow, isDirect);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = sale.TenantId,
                    EntryKind = "sale",
                    SourceKind = "concession",
                    SourceId = sale.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = sale.TotalCents,
                    StripeFeeCents = stripeFee,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = calc.NetToTenantCents,
                    StripePaymentIntentId = sale.StripePaymentIntentId,
                    PaymentMethod = isDirect ? "stripe_direct" : "stripe",
                    SoldByUserId = sale.SoldByUserId,   // cashier on counter card sales; null for online orders
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Ledger entry for concession sale {Id} already exists; skipping.", sale.Id);
            }
        }

        private async Task SendPurchaseEmailAsync(Guid tenantId, string toEmail, string toName, Guid redemptionToken,
            string kind, int amountCents, DateTime? validOnDate, bool isGuest)
        {
            if (!_emailer.IsConfigured) return;
            // Skip hard-bounced addresses (scope='all'); a dead address only inflates the
            // account-wide bounce rate. Marketing opt-outs don't block a transactional receipt.
            if (await _suppression.IsSuppressed(toEmail, tenantId, marketing: false)) return;
            try
            {
                var tenant = await _tenants.GetById(tenantId);
                if (tenant is null) return;
                var apex = _config["App:RootDomain"] ?? "ridepass.io";
                var baseUrl = $"https://{tenant.Subdomain}.{apex}";
                var qrUrl = $"{baseUrl}/api/Qr/{redemptionToken}";
                var profileUrl = $"{baseUrl}/User/MyPasses";

                var (subject, kindLabel) = kind switch
                {
                    "pass" => ($"Your {tenant.DisplayName} day pass", "pass"),
                    "event_ticket" => ($"Your {tenant.DisplayName} admission", "admission"),
                    "season_pass" => ($"Your {tenant.DisplayName} season pass", "season pass"),
                    _ => ($"Your {tenant.DisplayName} purchase", "purchase"),
                };

                var validLine = validOnDate.HasValue
                    ? $"<p>Valid on <strong>{validOnDate.Value:dddd, MMMM d, yyyy}</strong>.</p>"
                    : string.Empty;

                // Guests have no account, so point them at signup (email prefilled) instead of a
                // My-Passes link they can't use; logged-in riders get the account link as before.
                var accountLine = isGuest
                    ? $@"<p>Want to manage your entries, check in faster next time, and get race-day and waitlist alerts?
<a href=""{baseUrl}/SignUp?email={Uri.EscapeDataString(toEmail)}"">Create your free account</a>.</p>"
                    : $@"<p>You can also find this {kindLabel} on your account at <a href=""{profileUrl}"">{profileUrl}</a>.</p>";

                var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(toName)},</p>
<p>Thanks for your {kindLabel} from <strong>{System.Net.WebUtility.HtmlEncode(tenant.DisplayName)}</strong>.
Total: <strong>${(amountCents / 100m):0.00}</strong>.</p>
{validLine}
<p>Show this QR at the gate to be checked in:</p>
<p><img src=""{qrUrl}"" alt=""Your QR code"" width=""240"" height=""240"" style=""border:1px solid #ddd; padding:6px; background:#fff"" /></p>
<p>If your email client doesn't show the image, open <a href=""{qrUrl}"">this link</a> on your phone — it'll display the QR.</p>
{accountLine}";

                await _emailer.Send(toEmail, subject, html);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send {Kind} confirmation email to {Email}", kind, toEmail);
            }
        }

        // Race-entry bundled coupons: separate email so the rider has the codes inline,
        // not buried inside the regular ticket confirmation. Sent best-effort — failure
        // here doesn't undo the purchase.
        private async Task SendBundledCouponEmailAsync(Guid tenantId,
            Services.Repositories.Data.PaymentData.EventTicketPurchase ticket,
            Services.Repositories.Data.PaymentData.EventTicketTier tier,
            List<Services.Repositories.Data.CouponData.Coupon> coupons)
        {
            if (!_emailer.IsConfigured) return;
            try
            {
                var tenant = await _tenants.GetById(tenantId);
                if (tenant is null) return;
                var apex = _config["App:RootDomain"] ?? "ridepass.io";
                var profileUrl = $"https://{tenant.Subdomain}.{apex}/User/MyPasses";

                var discountLabel = tier.BundledCouponDiscountKind == "percent"
                    ? $"{tier.BundledCouponDiscountValue / 100}% off"
                    : $"${tier.BundledCouponDiscountValue / 100m:0.00} off";

                var rows = string.Join("",
                    coupons.Select(c => $"<tr><td style=\"padding:6px 12px;border:1px solid #ddd;font-family:monospace\"><strong>{System.Net.WebUtility.HtmlEncode(c.Code)}</strong></td></tr>"));

                var subject = $"Your {tenant.DisplayName} coupons";
                var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(ticket.PurchaserName)},</p>
<p>Thanks for entering <strong>{System.Net.WebUtility.HtmlEncode(tier.Name)}</strong>! As part of your race entry you've received
<strong>{coupons.Count} coupon{(coupons.Count == 1 ? "" : "s")}</strong> — each one is worth <strong>{discountLabel}</strong> and is single-use.</p>
<p>Share these with friends and family so they can come watch you ride:</p>
<table style=""border-collapse:collapse"">{rows}</table>
<p>You can also find them on your <a href=""{profileUrl}"">My Passes</a> page, where you can email them directly to friends.</p>";

                await _emailer.Send(ticket.PurchaserEmail, subject, html);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send bundled-coupon email for ticket {Id}", ticket.Id);
            }
        }

        private async Task OnPaymentSucceeded(
            string paymentIntentId,
            List<EventTicketPurchase> tickets,
            bool isDirect)
        {
            // Direct charge: the tenant's own account bore the Stripe fee, so we record none on our
            // ledger (and skip the fee lookup, which would target the platform account anyway).
            var totalStripeFee = isDirect ? 0 : (await _payments.GetActualStripeFeeCentsAsync(paymentIntentId) ?? 0);

            // Pro-rata distribute the single PI-level Stripe fee across all line items by gross.
            // MarkPaid updates BOTH the DB and the in-memory POCO so the per-purchase
            // confirmation email loop below (which filters on Status == "paid") finds
            // the rows we just flipped. Without the in-memory mutation the email loop
            // matches zero rows on the first webhook delivery — guests get no receipt.
            var lines = tickets
                .Where(t => t.Status != "paid" && t.Status != "redeemed")
                .Select(t => (Kind: "event_ticket", Id: t.Id, TenantId: t.TenantId, Gross: t.AmountCents,
                              ServiceCharge: t.ServiceChargeCents,
                              RewardRedemptionId: t.AppliedRewardRedemptionId,
                              MarkPaid: (Func<Task>)(async () => {
                                  await _ticketPurchases.UpdateStatus(t.Id, "paid");
                                  t.Status = "paid";
                              })))
                .ToList();
            if (lines.Count == 0) return;

            var totalGross = lines.Sum(l => (long)l.Gross);
            var feeDistributed = 0;
            var occurredAt = DateTime.UtcNow;

            // Notify super admins on first-time processing of a large sale (above configurable threshold).
            // Done before per-line work so duplicate webhook fires (where lines is empty after the .Where filter) won't re-notify.
            if (_largeSaleThresholdCents > 0 && totalGross >= _largeSaleThresholdCents)
            {
                await _notifications.EmitToSuperAdmins(
                    kind: "large_sale",
                    title: $"Large sale: ${(totalGross / 100m):0.00}",
                    body: $"Tenant collected ${(totalGross / 100m):0.00} on payment_intent {paymentIntentId} ({lines.Count} line item{(lines.Count == 1 ? "" : "s")}).",
                    linkUrl: "/SuperAdmin",
                    tenantId: lines[0].TenantId);
            }

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var stripeFeeForLine = i == lines.Count - 1
                    ? totalStripeFee - feeDistributed
                    : (int)(totalStripeFee * line.Gross / totalGross);
                feeDistributed += stripeFeeForLine;

                var calc = await _feeCalculator.Calculate(line.TenantId, line.Gross, stripeFeeForLine, line.ServiceCharge, occurredAt, isDirect);

                await line.MarkPaid();

                try
                {
                    await _ledger.Insert(new TenantLedgerEntry
                    {
                        TenantId = line.TenantId,
                        EntryKind = "sale",
                        SourceKind = line.Kind,
                        SourceId = line.Id,
                        OccurredAtUtc = occurredAt,
                        GrossCents = line.Gross,
                        StripeFeeCents = stripeFeeForLine,
                        RidepassCutCents = calc.RidepassCutCents,
                        NetToTenantCents = calc.NetToTenantCents,
                        AppliedTierId = calc.AppliedTierId,
                        CumulativeMonthlyVolumeAtSaleCents = calc.CumulativeMonthlyVolumeAtSaleCents,
                        StripePaymentIntentId = paymentIntentId,
                        PaymentMethod = isDirect ? "stripe_direct" : "stripe",
                    });
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
                {
                    // Idempotent: duplicate (tenant_id, source_kind, source_id) for entry_kind='sale'.
                    // Webhook fired again for an already-recorded sale — safe to ignore.
                    _logger.LogDebug("Ledger entry for {Kind} {Id} already exists; skipping.", line.Kind, line.Id);
                }

                if (line.RewardRedemptionId.HasValue)
                {
                    await _rewards.MarkRedemptionUsed(line.RewardRedemptionId.Value, line.Kind, line.Id);
                }

                // Phase-2 race-entry bundles: when the just-paid line is an event ticket whose
                // tier carries a bundled-coupon config, mint the codes for the buyer. Idempotent
                // — duplicate webhook deliveries find existing rows and short-circuit.
                if (line.Kind == "event_ticket")
                {
                    var ticketRow = tickets.FirstOrDefault(t => t.Id == line.Id);
                    if (ticketRow is not null)
                    {
                        var tier = await _tiers.GetById(ticketRow.TierId, line.TenantId);
                        if (tier is not null && tier.BundledCouponCount is > 0)
                        {
                            // Bundled coupons are a tenant feature toggle: turning it off stops
                            // minting new codes even on tiers configured while it was on
                            // (already-issued codes stay redeemable).
                            var mintTenant = await _tenants.GetById(line.TenantId);
                            if (mintTenant?.BundledCouponsEnabled == true)
                            {
                                var minted = await _bundledCouponMinter.MintForPurchase(
                                    tier, line.TenantId, ticketRow.Id, ticketRow.PurchaserUserId);
                                if (minted.Count > 0)
                                {
                                    _ = SendBundledCouponEmailAsync(line.TenantId, ticketRow, tier, minted);
                                }
                            }
                        }
                    }
                }
            }

            // Per-purchase confirmation emails with the QR code so riders have it in their
            // inbox even if they're not logged in (guest ticket purchases especially).
            foreach (var t in tickets.Where(p => p.Status == "paid"))
            {
                _ = SendPurchaseEmailAsync(t.TenantId, t.PurchaserEmail, t.PurchaserName, t.RedemptionToken,
                    "event_ticket", t.AmountCents, null, isGuest: t.PurchaserUserId is null);
            }

            // Run loyalty rewards once per (tenant, rider). Guest ticket purchases (no user) are skipped.
            var rewardActors = tickets
                .Where(t => t.PurchaserUserId.HasValue)
                .Select(t => (TenantId: t.TenantId, UserId: t.PurchaserUserId, Email: t.PurchaserEmail, Name: t.PurchaserName))
                .Where(a => a.UserId.HasValue)
                .GroupBy(a => (a.TenantId, a.UserId!.Value))
                .Select(g => g.First());
            foreach (var actor in rewardActors)
            {
                try
                {
                    var firstName = actor.Name?.Split(' ').FirstOrDefault() ?? "rider";
                    await _rewardEngine.ProcessPaidPurchase(actor.TenantId, actor.UserId!.Value, actor.Email, firstName);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Reward engine failed for tenant {TenantId} user {UserId}", actor.TenantId, actor.UserId);
                }
            }
        }
    }
}
