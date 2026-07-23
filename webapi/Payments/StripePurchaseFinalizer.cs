using Services.Helpers;
using Services.Notifications;
using Services.Payments;
using Services.Repositories.Data.ExtrasData;
using Services.Repositories.Data.PaymentData;
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
        private readonly IEventWaitlistRepository _waitlist;
        private readonly IEventExtraRepository _extras;
        private readonly IMembershipRepository _memberships;
        private readonly IConcessionRepository _concessions;
        private readonly IBikeShopRepository _shop;
        private readonly ITenantCreditRepository _credit;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ISmtpEmailer _emailer;
        private readonly Services.Email.IEventOrderConfirmationEmailer _orderConfirmations;
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
            IEventWaitlistRepository waitlist,
            IEventExtraRepository extras,
            IMembershipRepository memberships,
            IConcessionRepository concessions,
            IBikeShopRepository shop,
            ITenantCreditRepository credit,
            IEmailSuppressionRepository suppression,
            ISmtpEmailer emailer,
            Services.Email.IEventOrderConfirmationEmailer orderConfirmations,
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
            _waitlist = waitlist;
            _extras = extras;
            _memberships = memberships;
            _concessions = concessions;
            _shop = shop;
            _credit = credit;
            _suppression = suppression;
            _emailer = emailer;
            _orderConfirmations = orderConfirmations;
            _config = configuration;
            _logger = logger;
            _largeSaleThresholdCents = configuration.GetValue<int?>("Notifications:LargeSaleThresholdCents") ?? 50_000;  // $500 default
        }

        public async Task ProcessPaymentIntentAsync(string paymentIntentId, string eventType, CancellationToken ct = default)
        {
            // A counter sale can attach multiple purchase rows (mixed kinds) to one PaymentIntent,
            // so iterate everything that points at this PI rather than stopping after the first match.
            var tickets = await _ticketPurchases.ListByStripePaymentIntentId(paymentIntentId);
            // One checkout can buy several passes (a parent buying for their kids), all sharing
            // this PaymentIntent — so every row must be finalized, not just the first.
            var seasonPasses = await _seasonPasses.ListPurchasesByStripePaymentIntentId(paymentIntentId);
            var giftCard = await _giftCards.GetByPaymentIntentId(paymentIntentId);
            var waitlistPrepay = await _waitlist.GetByPrepayPaymentIntentId(paymentIntentId);
            var extras = await _extras.ListByPaymentIntentId(paymentIntentId);
            var membership = await _memberships.GetByPaymentIntentId(paymentIntentId);
            var concessionSale = await _concessions.GetSaleByPaymentIntentId(paymentIntentId);
            var shopSale = await _shop.GetSaleByPaymentIntentId(paymentIntentId);
            var shopRental = await _shop.GetRentalByFeePaymentIntentId(paymentIntentId);
            var shopWoDeposit = await _shop.GetWorkOrderByDepositPaymentIntentId(paymentIntentId);

            // A rental security-deposit HOLD is a manual-capture PI on deposit_pi_id. Its whole
            // lifecycle (authorize at booking, capture-for-damage or cancel at return) is driven
            // by BikeShopRentalController, not by this webhook: the authorization fires
            // amount_capturable_updated and a later damage capture fires succeeded, but neither is a
            // sale to book here. Recognize it and return quietly so it doesn't fall through to the
            // "unknown payment_intent" warning (or, worse, get treated as a bookable charge).
            var shopDepositHold = await _shop.GetRentalByDepositPaymentIntentId(paymentIntentId);
            if (shopDepositHold is not null)
            {
                return;
            }

            if (tickets.Count == 0 && seasonPasses.Count == 0 && giftCard is null && waitlistPrepay is null && extras.Count == 0 && membership is null && concessionSale is null && shopSale is null && shopRental is null && shopWoDeposit is null)
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
                ?? seasonPasses.FirstOrDefault(sp => !string.IsNullOrEmpty(sp.StripeConnectedAccountId))?.StripeConnectedAccountId
                ?? shopRental?.StripeConnectedAccountId;
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
                if (tickets.Count == 0 && seasonPasses.Count == 0 && extras.Count == 0)
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
                        && membership is null && seasonPasses.Count == 0;
                    await OnExtrasPaid(paymentIntentId, extras, extrasOwnTheFee, isDirect, ticketsOnPi: tickets.Count > 0);
                }
                else if (eventType == "payment_intent.payment_failed")
                {
                    foreach (var x in extras.Where(e => e.Status == "pending"))
                        await _extras.UpdateStatus(x.Id, "failed");
                }
                if (tickets.Count == 0 && seasonPasses.Count == 0)
                {
                    return;
                }
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
                    // Hand back any store credit the POS redeemed as a tender.
                    if (concessionSale.CreditAppliedCents > 0)
                        await _credit.ReverseRedeem(concessionSale.TenantId, "concession_sale", concessionSale.Id, "payment failed");
                }
                return;
            }

            // Bike shop retail sale: standalone on its own PaymentIntent, same shape as concessions.
            if (shopSale is not null)
            {
                if (eventType == "payment_intent.succeeded" && shopSale.Status == "pending")
                {
                    await OnShopSalePaid(shopSale);
                }
                else if (eventType == "payment_intent.payment_failed" && shopSale.Status == "pending")
                {
                    await _shop.MarkSaleFailed(shopSale.Id);
                    // Hand back any coupon use the register recorded at ring-up.
                    await RestoreDiscountsFor("shop_sale", new[] { shopSale.Id });
                    // And any store credit it redeemed as a tender.
                    if (shopSale.CreditAppliedCents > 0)
                        await _credit.ReverseRedeem(shopSale.TenantId, "shop_sale", shopSale.Id, "payment failed");
                }
                return;
            }

            // Bike shop rental fee (the deposit hold was already recognized and skipped above).
            // A lesson bike rides the same PaymentIntent as the lesson tickets, so mirror the
            // extras rule: when tickets share the PI, keep going (they finalize below) and don't
            // double-count the Stripe fee (the tickets absorb it).
            if (shopRental is not null)
            {
                if (eventType == "payment_intent.succeeded" && shopRental.Status == "pending")
                {
                    var rentalOwnsTheFee = tickets.Count == 0 && extras.Count == 0
                        && seasonPasses.Count == 0 && membership is null;
                    await OnShopRentalPaid(shopRental, rentalOwnsTheFee);
                }
                else if (eventType == "payment_intent.payment_failed" && shopRental.Status == "pending")
                {
                    await _shop.MarkRentalFailed(shopRental.Id);
                }
                if (tickets.Count == 0 && seasonPasses.Count == 0)
                {
                    return;
                }
            }

            // Bike shop repair deposit paid via the emailed payment link. Immediate capture (a
            // prepayment, not a hold): book its own ledger entry now; bill-out later credits it
            // against the sale and records only the remainder.
            if (shopWoDeposit is not null)
            {
                if (eventType == "payment_intent.succeeded" && shopWoDeposit.DepositPaidAt is null)
                {
                    await OnShopWoDepositPaid(shopWoDeposit);
                }
                else if (eventType == "payment_intent.payment_failed" && shopWoDeposit.DepositPaidAt is null)
                {
                    // Drop the dead PI so the customer's next visit to the link can start fresh.
                    await _shop.ClearWorkOrderDepositIntent(shopWoDeposit.Id, shopWoDeposit.TenantId);
                }
                return;
            }

            switch (eventType)
            {
                case "payment_intent.succeeded":
                    await OnPaymentSucceeded(paymentIntentId, tickets, isDirect);
                    var pendingPasses = seasonPasses.Where(sp => sp.Status == "pending").ToList();
                    if (pendingPasses.Count > 0)
                    {
                        // Tickets on the same PI already absorbed the PI's Stripe fee in their own
                        // ledger rows, so the passes must not count it a second time.
                        await OnSeasonPassesPaid(paymentIntentId, pendingPasses, isDirect,
                            passesOwnTheFee: tickets.Count == 0);
                    }
                    // Store credit applied to this checkout: the per-row entries above booked their
                    // full grosses, so one balancing entry nets the books down to what the PI
                    // actually collected (Script0195). Direct-mode rows already carry net 0
                    // (the tenant holds the funds), so only platform-held money reduces net.
                    var paidTender = await _credit.GetCheckoutTenderByPaymentIntentId(paymentIntentId);
                    if (paidTender is not null) await BookCreditTenderEntry(paidTender, reduceNet: !isDirect);
                    break;
                case "payment_intent.payment_failed":
                    var failedTickets = tickets.Where(p => p.Status == "pending").ToList();
                    foreach (var t in failedTickets)
                        await _ticketPurchases.UpdateStatus(t.Id, "failed");
                    if (failedTickets.Count > 0)
                        await RestoreDiscountsFor("event_ticket", failedTickets.Select(t => t.Id).ToList());
                    // Credit-funded tickets on a dead payment: hand the ride back to the funding
                    // pass. ClearAppliedSeasonPass is single-winner, so a webhook/reconciler race
                    // can't double-credit.
                    foreach (var t in failedTickets.Where(t => t.AppliedSeasonPassPurchaseId.HasValue))
                    {
                        var cleared = await _ticketPurchases.ClearAppliedSeasonPass(t.Id, t.TenantId);
                        if (cleared > 0)
                            await _seasonPasses.IncrementCredits(t.AppliedSeasonPassPurchaseId!.Value, t.TenantId);
                    }
                    foreach (var sp in seasonPasses.Where(p => p.Status == "pending"))
                        await _seasonPasses.UpdatePurchaseStatus(sp.Id, "failed");
                    // Hand back any store credit this checkout debited.
                    var failedTender = await _credit.GetCheckoutTenderByPaymentIntentId(paymentIntentId);
                    if (failedTender is not null)
                        await _credit.ReverseRedeem(failedTender.TenantId, "credit_tender", failedTender.Id, "payment failed");
                    break;
            }
        }

        /// <summary>
        /// The tender's balancing ledger entry: gross -credit so revenue sums match money in
        /// (the per-row entries booked full grosses). The platform cut stays charged in full,
        /// matching the cash convention: credit-covered value is tenant-funded. reduceNet is
        /// true only when the rows booked platform-held money (a platform-mode Stripe checkout,
        /// where the PI collected credit-less); cash-convention rows and direct-mode rows
        /// already reflect reality, so their tender only corrects gross.
        /// Idempotent via the one-sale-entry-per-source ledger index.
        /// </summary>
        public async Task BookCreditTenderEntry(
            Services.Repositories.Data.CreditData.CheckoutCreditTender tender, bool reduceNet)
        {
            try
            {
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = tender.TenantId,
                    EntryKind = "sale",
                    SourceKind = "credit_tender",
                    SourceId = tender.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = -tender.CreditAppliedCents,
                    StripeFeeCents = 0,
                    RidepassCutCents = 0,
                    NetToTenantCents = reduceNet ? -tender.CreditAppliedCents : 0,
                    StripePaymentIntentId = tender.StripePaymentIntentId,
                    PaymentMethod = "credit",
                    Memo = "Store credit applied at checkout",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Credit tender entry {Id} already booked; skipping.", tender.Id);
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

        /// <summary>
        /// Finalizes every season pass on one PaymentIntent. A single checkout can buy several
        /// passes, so the intent's one Stripe fee is pro-rata distributed across them by gross
        /// (last pass takes the rounding remainder) — charging each pass the whole intent fee
        /// would multiply it by the pass count and understate what the tenant is owed.
        /// </summary>
        private async Task OnSeasonPassesPaid(string paymentIntentId, List<SeasonPassPurchase> passes,
            bool isDirect, bool passesOwnTheFee)
        {
            // Direct charge: the tenant's account bore the Stripe fee, so we record none (and skip
            // the lookup, which targets the platform account anyway). Same when tickets share this
            // intent — they've already booked the fee.
            var totalStripeFee = isDirect || !passesOwnTheFee
                ? 0
                : (await _payments.GetActualStripeFeeCentsAsync(paymentIntentId) ?? 0);

            var totalGross = passes.Sum(p => (long)p.AmountCents);
            var feeDistributed = 0;
            var occurredAt = DateTime.UtcNow;

            for (var i = 0; i < passes.Count; i++)
            {
                var pass = passes[i];
                var stripeFeeForPass = i == passes.Count - 1
                    ? totalStripeFee - feeDistributed
                    : totalGross > 0 ? (int)(totalStripeFee * pass.AmountCents / totalGross) : 0;
                feeDistributed += stripeFeeForPass;

                await _seasonPasses.UpdatePurchaseStatus(pass.Id, "paid");
                pass.Status = "paid";
                try
                {
                    var calc = await _feeCalculator.Calculate(pass.TenantId, pass.AmountCents, stripeFeeForPass,
                        pass.ServiceChargeCents, occurredAt, isDirect);
                    await _ledger.Insert(new TenantLedgerEntry
                    {
                        TenantId = pass.TenantId,
                        EntryKind = "sale",
                        SourceKind = "season_pass",
                        SourceId = pass.Id,
                        OccurredAtUtc = occurredAt,
                        GrossCents = pass.AmountCents,
                        StripeFeeCents = stripeFeeForPass,
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

                // One email per pass: each carries its own QR token, so a buyer who bought passes
                // for their family gets one forwardable message per holder. Season passes are always
                // bought by a logged-in rider (PurchaserUserId is non-nullable), so no guest case.
                _ = SendPurchaseEmailAsync(pass.TenantId, pass.PurchaserEmail, pass.PurchaserName, pass.RedemptionToken,
                    "season_pass", pass.AmountCents, null, isGuest: false);
            }
        }

        // Gate fees and other add-ons: flip pending rows to paid AND write a sale ledger
        // entry per row so the revenue reaches the tenant's balance/payout. Previously these
        // only flipped status and never hit the ledger, so spectator/add-on income was lost
        // from payouts. Idempotent: the unique (tenant, source_kind, source_id) sale index
        // makes a duplicate webhook/reconciler pass a no-op.
        // ticketsOnPi: when the cart also holds event tickets, the ticket confirmation already lists
        // these add-ons (it's built from the whole event order), so emailing here too would double up.
        // An add-ons-only cart (a spectator gate fee, camping) has no other sender, so it confirms here.
        private async Task OnExtrasPaid(string paymentIntentId, List<EventExtraPurchase> extras,
            bool extrasOwnTheFee, bool isDirect, bool ticketsOnPi)
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

            if (!ticketsOnPi)
            {
                await _orderConfirmations.SendForExtras(pending[0].TenantId, pending.Select(x => x.Id).ToList());
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
                // Store credit never moves money (booked when funded), so the entry records only
                // what the PI actually collected.
                var collected = sale.TotalCents - sale.CreditAppliedCents;
                var calc = await _feeCalculator.Calculate(sale.TenantId, collected, stripeFee, 0, DateTime.UtcNow, isDirect);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = sale.TenantId,
                    EntryKind = "sale",
                    SourceKind = "concession",
                    SourceId = sale.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = collected,
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

            try
            {
                await _rewardEngine.AwardCreditBack(sale.TenantId, sale.PurchaserUserId, sale.PurchaserEmail,
                    sale.PurchaserName, "concession", sale.Id, sale.TotalCents - sale.CreditAppliedCents);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Credit-back failed for concession sale {Id}", sale.Id);
            }
        }

        private async Task OnShopSalePaid(Services.Repositories.Data.BikeShopData.ShopSale sale)
        {
            // Gate on the paid flip so the order number, depletion, and ledger run exactly once even
            // if the webhook and the reconciler both fire.
            if (!await _shop.TryMarkSalePaid(sale.Id, sale.TenantId)) return;

            var orderNumber = await _shop.NextOrderNumber(sale.TenantId);
            await _shop.SetSaleOrderNumber(sale.Id, orderNumber);
            try { await _shop.DepleteForSale(sale.Id, sale.TenantId, sale.SoldByUserId); }
            catch { /* inventory depletion is best-effort; the sale is paid regardless */ }
            // A bill-out sale settles its work order (no-op for ordinary register sales).
            try { await _shop.MarkWorkOrderPickedUpBySale(sale.Id); }
            catch { /* status roll is best-effort */ }

            // An online order's confirmation IS the pickup claim: nothing else emails the buyer.
            if (sale.OrderChannel == "online" && !string.IsNullOrWhiteSpace(sale.BuyerEmail))
            {
                try
                {
                    var tenant = await _tenants.GetById(sale.TenantId);
                    if (tenant is not null && _emailer.IsConfigured
                        && !await _suppression.IsSuppressed(sale.BuyerEmail!, sale.TenantId, marketing: false))
                    {
                        static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
                        var firstName = sale.BuyerName?.Split(' ').FirstOrDefault() ?? "rider";
                        var html = $@"<div style=""font-family:Arial,Helvetica,sans-serif;max-width:480px"">
<h2 style=""margin:0 0 8px"">{Enc(tenant.DisplayName)}</h2>
<p>Hi {Enc(firstName)}, your shop order is confirmed.</p>
<p style=""font-size:22px;font-weight:bold;margin:8px 0"">Order #{orderNumber}</p>
<p>Show this number at the counter to pick it up. Total paid: <strong>{"$" + ((sale.TotalCents - sale.CreditAppliedCents) / 100m).ToString("0.00")}</strong>{(sale.CreditAppliedCents > 0 ? $" (plus {"$" + (sale.CreditAppliedCents / 100m).ToString("0.00")} store credit)" : "")}.</p></div>";
                        await _emailer.Send(sale.BuyerEmail!, $"{tenant.DisplayName}: shop order #{orderNumber} confirmed",
                            html, null, Services.Email.TenantEmailIdentity.For(tenant));
                    }
                }
                catch { /* the order stands; email is best-effort */ }
            }

            // Low-stock sweep (same pattern as concessions; de-duped per episode, best-effort).
            try
            {
                var low = await _shop.MarkAndGetNewlyLowShopStock(sale.TenantId);
                if (low.Count > 0)
                {
                    var names = string.Join(", ", low.Select(i =>
                        $"{i.ProductName}{(i.VariantLabel is null ? "" : $" ({i.VariantLabel})")} — {i.Available} left"));
                    var title = low.Count == 1 ? "1 shop item low on stock" : $"{low.Count} shop items low on stock";
                    await _notifications.EmitToTenantRoles(sale.TenantId, new[] { "tenant_manager", "tenant_admin" },
                        NotificationKinds.LowStock, title, $"Running low: {names}.", "/Admin/BikeShop");
                }
            }
            catch { /* alerting is best-effort */ }

            // Direct mode: the tenant's account bore the Stripe fee, so we record none.
            var isDirect = !string.IsNullOrEmpty(sale.StripeConnectedAccountId);
            var stripeFee = isDirect ? 0 : (await _payments.GetActualStripeFeeCentsAsync(sale.StripePaymentIntentId!) ?? 0);
            try
            {
                // A work-order deposit already booked its own 'shop_wo_deposit' entry when it was
                // paid, and store credit never moves money at all (its value was booked when it
                // was funded), so both stay out of gross. A gift-card-funded portion stays IN:
                // gift purchases book nothing, so its revenue is recognized here at redemption
                // (the PI itself charged gross minus the gift amount).
                var collected = sale.TotalCents - sale.DepositAppliedCents - sale.CreditAppliedCents;
                var calc = await _feeCalculator.Calculate(sale.TenantId, collected, stripeFee, 0, DateTime.UtcNow, isDirect);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = sale.TenantId,
                    EntryKind = "sale",
                    SourceKind = "shop_sale",
                    SourceId = sale.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = collected,
                    StripeFeeCents = stripeFee,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = calc.NetToTenantCents,
                    StripePaymentIntentId = sale.StripePaymentIntentId,
                    PaymentMethod = isDirect ? "stripe_direct" : "stripe",
                    SoldByUserId = sale.SoldByUserId,
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Ledger entry for shop sale {Id} already exists; skipping.", sale.Id);
            }

            try
            {
                await _rewardEngine.AwardCreditBack(sale.TenantId, sale.BuyerUserId, sale.BuyerEmail, sale.BuyerName,
                    "shop_sale", sale.Id, sale.TotalCents - sale.DepositAppliedCents - sale.CreditAppliedCents);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Credit-back failed for shop sale {Id}", sale.Id);
            }
        }

        private async Task OnShopWoDepositPaid(Services.Repositories.Data.BikeShopData.ShopWorkOrder wo)
        {
            // Gate on the paid flip so the ledger entry books exactly once even if the webhook and
            // the reconciler both fire (mirrors TryMarkSalePaid).
            var isDirect = !string.IsNullOrEmpty(wo.DepositStripeAccountId);
            if (!await _shop.TryMarkWorkOrderDepositPaid(wo.Id, wo.TenantId, isDirect ? "stripe_direct" : "stripe")) return;

            var stripeFee = isDirect ? 0 : (await _payments.GetActualStripeFeeCentsAsync(wo.DepositPiId!) ?? 0);
            try
            {
                var calc = await _feeCalculator.Calculate(wo.TenantId, wo.DepositCents, stripeFee, 0, DateTime.UtcNow, isDirect);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = wo.TenantId,
                    EntryKind = "sale",
                    SourceKind = "shop_wo_deposit",
                    SourceId = wo.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = wo.DepositCents,
                    StripeFeeCents = stripeFee,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = calc.NetToTenantCents,
                    StripePaymentIntentId = wo.DepositPiId,
                    PaymentMethod = isDirect ? "stripe_direct" : "stripe",
                    Memo = "Bike shop repair deposit",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Ledger entry for work-order deposit {Id} already exists; skipping.", wo.Id);
            }
        }

        private async Task OnShopRentalPaid(Services.Repositories.Data.BikeShopData.ShopRental rental, bool ownsFee = true)
        {
            if (!await _shop.TryMarkRentalPaid(rental.Id, rental.TenantId)) return;

            var orderNumber = await _shop.NextOrderNumber(rental.TenantId);
            await _shop.SetRentalOrderNumber(rental.Id, orderNumber);

            // Gross is the rental FEE only. The deposit hold is not revenue — it's the rider's
            // money under authorization; only a damage capture (booked as shop_rental_deposit at
            // return time) ever becomes a ledger entry. When the rental shares its PI with lesson
            // tickets (ownsFee = false) the tickets absorb the Stripe fee, so book zero here.
            var isDirect = !string.IsNullOrEmpty(rental.StripeConnectedAccountId);
            var stripeFee = (isDirect || !ownsFee) ? 0
                : (await _payments.GetActualStripeFeeCentsAsync(rental.StripePaymentIntentId!) ?? 0);
            try
            {
                // Pass the rental's frozen service charge so RidePass's cut is booked. It is what the
                // track owes regardless of whether they passed it to the renter or absorbed it.
                var calc = await _feeCalculator.Calculate(rental.TenantId, rental.TotalCents, stripeFee, rental.ServiceChargeCents, DateTime.UtcNow, isDirect);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = rental.TenantId,
                    EntryKind = "sale",
                    SourceKind = "shop_rental",
                    SourceId = rental.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = rental.TotalCents,
                    StripeFeeCents = stripeFee,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = calc.NetToTenantCents,
                    StripePaymentIntentId = rental.StripePaymentIntentId,
                    PaymentMethod = isDirect ? "stripe_direct" : "stripe",
                    SoldByUserId = rental.SoldByUserId,
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Ledger entry for shop rental {Id} already exists; skipping.", rental.Id);
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

                await _emailer.Send(toEmail, subject, html, null, Services.Email.TenantEmailIdentity.For(tenant));
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

                await _emailer.Send(ticket.PurchaserEmail, subject, html, null, Services.Email.TenantEmailIdentity.For(tenant));
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

            // One confirmation per event order (not per ticket row): a rider who bought a gate fee
            // plus three classes gets a single email listing all of it with one QR, and any add-ons
            // on the same order ride along in that email. Riders who aren't logged in have no other
            // way to get their QR, so this is the whole delivery path for a guest.
            var paidTicketIds = tickets.Where(p => p.Status == "paid").Select(p => p.Id).ToList();
            if (paidTicketIds.Count > 0)
            {
                var emailTenantId = tickets.First(p => p.Status == "paid").TenantId;
                await _orderConfirmations.SendForTickets(emailTenantId, paidTicketIds);
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

            // Credit-back loyalty: once per (tenant, rider) on this checkout's ticket spend, keyed
            // to the rider's lowest ticket id so webhook + reconciler re-fires stay idempotent.
            var creditBackActors = tickets
                .Where(t => t.Status == "paid" && t.PurchaserUserId.HasValue)
                .GroupBy(t => (t.TenantId, UserId: t.PurchaserUserId!.Value));
            foreach (var g in creditBackActors)
            {
                try
                {
                    var first = g.OrderBy(t => t.Id).First();
                    await _rewardEngine.AwardCreditBack(g.Key.TenantId, g.Key.UserId,
                        first.PurchaserEmail, first.PurchaserName,
                        "event_ticket", first.Id, g.Sum(t => t.AmountCents));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Credit-back failed for tenant {TenantId} user {UserId}", g.Key.TenantId, g.Key.UserId);
                }
            }
        }
    }
}
