using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Notifications;
using Services.Payments;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using Services.Rewards;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentProvider _payments;
        private readonly IPassPurchaseRepository _passPurchases;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IDisputeRepository _disputes;
        private readonly IFeeCalculator _feeCalculator;
        private readonly ITenantLedgerRepository _ledger;
        private readonly INotificationService _notifications;
        private readonly IRewardEngine _rewardEngine;
        private readonly IRewardRepository _rewards;
        private readonly IUserRepository _users;
        private readonly ISeasonPassRepository _seasonPasses;
        private readonly ITenantRepository _tenants;
        private readonly ITenantPayoutRepository _payouts;
        private readonly IEventTicketTierRepository _tiers;
        private readonly Services.Coupons.IBundledCouponMinter _bundledCouponMinter;
        private readonly IGiftCardRepository _giftCards;
        private readonly Services.GiftCards.IGiftCardDeliveryService _giftCardDelivery;
        private readonly IRentalRepository _rentals;
        private readonly IEventWaitlistRepository _waitlist;
        private readonly IEventExtraRepository _extras;
        private readonly IMembershipRepository _memberships;
        private readonly ISmtpEmailer _emailer;
        private readonly IConfiguration _config;
        private readonly ILogger<PaymentController> _logger;
        private readonly int _disputeFeeCents;

        public PaymentController(
            IPaymentProvider payments,
            IPassPurchaseRepository passPurchases,
            IEventTicketPurchaseRepository ticketPurchases,
            IDisputeRepository disputes,
            IFeeCalculator feeCalculator,
            ITenantLedgerRepository ledger,
            INotificationService notifications,
            IRewardEngine rewardEngine,
            IRewardRepository rewards,
            IUserRepository users,
            ISeasonPassRepository seasonPasses,
            ITenantRepository tenants,
            ITenantPayoutRepository payouts,
            IEventTicketTierRepository tiers,
            Services.Coupons.IBundledCouponMinter bundledCouponMinter,
            IGiftCardRepository giftCards,
            Services.GiftCards.IGiftCardDeliveryService giftCardDelivery,
            IRentalRepository rentals,
            IEventWaitlistRepository waitlist,
            IEventExtraRepository extras,
            IMembershipRepository memberships,
            ISmtpEmailer emailer,
            IConfiguration configuration,
            ILogger<PaymentController> logger)
        {
            _payments = payments;
            _passPurchases = passPurchases;
            _ticketPurchases = ticketPurchases;
            _disputes = disputes;
            _feeCalculator = feeCalculator;
            _ledger = ledger;
            _notifications = notifications;
            _rewardEngine = rewardEngine;
            _rewards = rewards;
            _users = users;
            _seasonPasses = seasonPasses;
            _tenants = tenants;
            _payouts = payouts;
            _tiers = tiers;
            _bundledCouponMinter = bundledCouponMinter;
            _giftCards = giftCards;
            _giftCardDelivery = giftCardDelivery;
            _rentals = rentals;
            _waitlist = waitlist;
            _extras = extras;
            _memberships = memberships;
            _emailer = emailer;
            _config = configuration;
            _logger = logger;
            // Stripe charges $15 USD per lost dispute (default). Override per-deploy via Stripe:DisputeFeeCents.
            _disputeFeeCents = configuration.GetValue<int?>("Stripe:DisputeFeeCents") ?? 1500;
            _largeSaleThresholdCents = configuration.GetValue<int?>("Notifications:LargeSaleThresholdCents") ?? 50_000;  // $500 default
        }

        private readonly int _largeSaleThresholdCents;

        [HttpPost("Webhook")]
        public async Task<IActionResult> StripeWebhook()
        {
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            var signature = Request.Headers["Stripe-Signature"].ToString();
            var webhookEvent = _payments.VerifyAndParseWebhook(rawBody, signature);
            if (webhookEvent is null)
            {
                return BadRequest();
            }

            if (webhookEvent.Dispute is not null)
            {
                await HandleDispute(webhookEvent.Dispute);
                return Ok();
            }

            if (webhookEvent.Account is not null && webhookEvent.Type == "account.updated")
            {
                // For Express + Transfer model what we care about is payouts_enabled (the bank
                // info is verified and Stripe will release funds); charges_enabled is a side
                // effect of the default Express capabilities. Keep both in 'active' to match
                // the existing UI semantics.
                var newStatus = !webhookEvent.Account.DetailsSubmitted ? "pending"
                              : (webhookEvent.Account.ChargesEnabled && webhookEvent.Account.PayoutsEnabled ? "active" : "restricted");
                await _tenants.UpdateStripeConnectStatus(webhookEvent.Account.AccountId, newStatus);
                return Ok();
            }

            if (webhookEvent.Transfer is not null)
            {
                await HandleTransferEvent(webhookEvent.Type, webhookEvent.Transfer);
                return Ok();
            }

            if (webhookEvent.PaymentIntentId is null)
            {
                return Ok();
            }

            // A counter sale can attach multiple purchase rows (mixed kinds) to one PaymentIntent,
            // so iterate everything that points at this PI rather than stopping after the first match.
            var passes = await _passPurchases.ListByStripePaymentIntentId(webhookEvent.PaymentIntentId);
            var tickets = await _ticketPurchases.ListByStripePaymentIntentId(webhookEvent.PaymentIntentId);
            var seasonPass = await _seasonPasses.GetPurchaseByStripePaymentIntentId(webhookEvent.PaymentIntentId);
            var giftCard = await _giftCards.GetByPaymentIntentId(webhookEvent.PaymentIntentId);
            var rental = await _rentals.GetPurchaseByRentalPaymentIntentId(webhookEvent.PaymentIntentId);
            var waitlistPrepay = await _waitlist.GetByPrepayPaymentIntentId(webhookEvent.PaymentIntentId);
            var extras = await _extras.ListByPaymentIntentId(webhookEvent.PaymentIntentId);
            var membership = await _memberships.GetByPaymentIntentId(webhookEvent.PaymentIntentId);

            if (passes.Count == 0 && tickets.Count == 0 && seasonPass is null && giftCard is null && rental is null && waitlistPrepay is null && extras.Count == 0 && membership is null)
            {
                _logger.LogWarning("Received Stripe event {EventType} for unknown payment_intent {IntentId}",
                    webhookEvent.Type, webhookEvent.PaymentIntentId);
                return Ok();
            }

            // Gift-card purchase: when the PI succeeds, immediate-delivery cards get
            // emailed inline; future-scheduled cards stay 'pending' for the worker.
            if (giftCard is not null && webhookEvent.Type == "payment_intent.succeeded")
            {
                if (!giftCard.ScheduledDeliveryAtUtc.HasValue || giftCard.ScheduledDeliveryAtUtc.Value <= DateTime.UtcNow)
                {
                    _ = _giftCardDelivery.SendDeliveryEmail(giftCard);
                }
                return Ok();
            }

            // Waitlist pre-pay: flip is_prepaid=true on success. Failure surfaces
            // as a normal Stripe failure; the row stays in 'waiting' (just not
            // pre-paid), so promotion goes through the regular pay-when-promoted
            // flow.
            if (waitlistPrepay is not null)
            {
                if (webhookEvent.Type == "payment_intent.succeeded")
                {
                    await _waitlist.MarkPrepaid(waitlistPrepay.Id, waitlistPrepay.PrepayAmountCents);
                }
                return Ok();
            }

            // Membership purchase: flip pending → paid + write a sale ledger entry.
            // Counter sales can bundle a membership with passes / tickets / extras on the
            // same PI, so we don't early-return when other kinds also matched.
            if (membership is not null)
            {
                if (webhookEvent.Type == "payment_intent.succeeded" && membership.Status == "pending")
                {
                    await OnMembershipPaid(membership);
                }
                else if (webhookEvent.Type == "payment_intent.payment_failed" && membership.Status == "pending")
                {
                    await _memberships.UpdateStatus(membership.Id, "failed");
                }
                if (passes.Count == 0 && tickets.Count == 0 && seasonPass is null && extras.Count == 0)
                {
                    return Ok();
                }
            }

            // Event extras (camping/parking/pit-vehicle/etc.): flip every line in the
            // cart that pointed at this PI to 'paid' or 'failed' as a group. Extras can
            // ride along with a pass purchase (Reserve a Spot upsell), so don't early-
            // return — fall through and let the pass/ticket switch run too.
            if (extras.Count > 0)
            {
                if (webhookEvent.Type == "payment_intent.succeeded")
                {
                    foreach (var x in extras.Where(e => e.Status == "pending"))
                        await _extras.UpdateStatus(x.Id, "paid");
                }
                else if (webhookEvent.Type == "payment_intent.payment_failed")
                {
                    foreach (var x in extras.Where(e => e.Status == "pending"))
                        await _extras.UpdateStatus(x.Id, "failed");
                }
                if (passes.Count == 0 && tickets.Count == 0 && seasonPass is null)
                {
                    return Ok();
                }
            }

            // Rental: flip pending → paid (reservation now firmly holds capacity).
            if (rental is not null)
            {
                if (webhookEvent.Type == "payment_intent.succeeded" && rental.Status == "pending")
                {
                    await _rentals.UpdateStatus(rental.Id, "paid");
                }
                else if (webhookEvent.Type == "payment_intent.payment_failed" && rental.Status == "pending")
                {
                    await _rentals.UpdateStatus(rental.Id, "failed");
                }
                return Ok();
            }

            switch (webhookEvent.Type)
            {
                case "payment_intent.succeeded":
                    await OnPaymentSucceeded(webhookEvent.PaymentIntentId, passes, tickets);
                    if (seasonPass is not null && seasonPass.Status == "pending")
                    {
                        await OnSeasonPassPaid(seasonPass);
                    }
                    break;
                case "payment_intent.payment_failed":
                    foreach (var dp in passes.Where(p => p.Status == "pending"))
                        await _passPurchases.UpdateStatus(dp.Id, "failed");
                    foreach (var t in tickets.Where(p => p.Status == "pending"))
                        await _ticketPurchases.UpdateStatus(t.Id, "failed");
                    if (seasonPass is not null && seasonPass.Status == "pending")
                        await _seasonPasses.UpdatePurchaseStatus(seasonPass.Id, "failed");
                    break;
            }
            return Ok();
        }

        private async Task OnMembershipPaid(Services.Repositories.Data.MembershipData.MembershipPurchase m)
        {
            var stripeFee = await _payments.GetActualStripeFeeCentsAsync(m.StripePaymentIntentId!) ?? 0;
            await _memberships.UpdateStatus(m.Id, "paid");
            try
            {
                var calc = await _feeCalculator.Calculate(m.TenantId, m.AmountCents, stripeFee, m.ServiceChargeCents, DateTime.UtcNow);
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
                    PaymentMethod = "stripe",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Ledger entry for membership {Id} already exists; skipping.", m.Id);
            }
        }

        private async Task OnSeasonPassPaid(SeasonPassPurchase pass)
        {
            var stripeFee = await _payments.GetActualStripeFeeCentsAsync(pass.StripePaymentIntentId!) ?? 0;
            await _seasonPasses.UpdatePurchaseStatus(pass.Id, "paid");
            try
            {
                var calc = await _feeCalculator.Calculate(pass.TenantId, pass.AmountCents, stripeFee, pass.ServiceChargeCents, DateTime.UtcNow);
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
                    PaymentMethod = "stripe",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Ledger entry for season_pass {Id} already exists; skipping.", pass.Id);
            }
            _ = SendPurchaseEmailAsync(pass.TenantId, pass.PurchaserEmail, pass.PurchaserName, pass.RedemptionToken,
                "season_pass", pass.AmountCents, null);
        }

        private async Task SendPurchaseEmailAsync(Guid tenantId, string toEmail, string toName, Guid redemptionToken,
            string kind, int amountCents, DateTime? validOnDate)
        {
            if (!_emailer.IsConfigured) return;
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

                var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(toName)},</p>
<p>Thanks for your {kindLabel} from <strong>{System.Net.WebUtility.HtmlEncode(tenant.DisplayName)}</strong>.
Total: <strong>${(amountCents / 100m):0.00}</strong>.</p>
{validLine}
<p>Show this QR at the gate to be checked in:</p>
<p><img src=""{qrUrl}"" alt=""Your QR code"" width=""240"" height=""240"" style=""border:1px solid #ddd; padding:6px; background:#fff"" /></p>
<p>If your email client doesn't show the image, open <a href=""{qrUrl}"">this link</a> on your phone — it'll display the QR.</p>
<p>You can also find this {kindLabel} on your account at <a href=""{profileUrl}"">{profileUrl}</a>.</p>";

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
            List<PassPurchase> passes,
            List<EventTicketPurchase> tickets)
        {
            var totalStripeFee = await _payments.GetActualStripeFeeCentsAsync(paymentIntentId) ?? 0;

            // Pro-rata distribute the single PI-level Stripe fee across all line items by gross.
            // MarkPaid updates BOTH the DB and the in-memory POCO so the per-purchase
            // confirmation email loop below (which filters on Status == "paid") finds
            // the rows we just flipped. Without the in-memory mutation the email loop
            // matches zero rows on the first webhook delivery — guests get no receipt.
            var lines = passes
                .Where(p => p.Status != "paid" && p.Status != "redeemed")
                .Select(p => (Kind: "pass", Id: p.Id, TenantId: p.TenantId, Gross: p.AmountCents,
                              ServiceCharge: p.ServiceChargeCents,
                              RewardRedemptionId: p.AppliedRewardRedemptionId,
                              MarkPaid: (Func<Task>)(async () => {
                                  await _passPurchases.UpdateStatus(p.Id, "paid");
                                  p.Status = "paid";
                              })))
                .Concat(tickets
                    .Where(t => t.Status != "paid" && t.Status != "redeemed")
                    .Select(t => (Kind: "event_ticket", Id: t.Id, TenantId: t.TenantId, Gross: t.AmountCents,
                                  ServiceCharge: t.ServiceChargeCents,
                                  RewardRedemptionId: t.AppliedRewardRedemptionId,
                                  MarkPaid: (Func<Task>)(async () => {
                                      await _ticketPurchases.UpdateStatus(t.Id, "paid");
                                      t.Status = "paid";
                                  }))))
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

                var calc = await _feeCalculator.Calculate(line.TenantId, line.Gross, stripeFeeForLine, line.ServiceCharge, occurredAt);

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
                        PaymentMethod = "stripe",
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

            // Per-purchase confirmation emails with the QR code so riders have it in their
            // inbox even if they're not logged in (guest ticket purchases especially).
            foreach (var dp in passes.Where(p => p.Status == "paid"))
            {
                _ = SendPurchaseEmailAsync(dp.TenantId, dp.PurchaserEmail, dp.PurchaserName, dp.RedemptionToken,
                    "pass", dp.AmountCents, dp.ValidOnDate);
            }
            foreach (var t in tickets.Where(p => p.Status == "paid"))
            {
                _ = SendPurchaseEmailAsync(t.TenantId, t.PurchaserEmail, t.PurchaserName, t.RedemptionToken,
                    "event_ticket", t.AmountCents, null);
            }

            // Run loyalty rewards once per (tenant, rider). Guest ticket purchases (no user) are skipped.
            var rewardActors = passes
                .Select(p => (TenantId: p.TenantId, UserId: (Guid?)p.PurchaserUserId, Email: p.PurchaserEmail, Name: p.PurchaserName))
                .Concat(tickets
                    .Where(t => t.PurchaserUserId.HasValue)
                    .Select(t => (TenantId: t.TenantId, UserId: t.PurchaserUserId, Email: t.PurchaserEmail, Name: t.PurchaserName)))
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

        /// <summary>
        /// Backstop for Stripe transfer reversals. Send-via-Stripe marks the payout 'paid' at
        /// Transfer.create time (settlement is synchronous on our side). This handler only
        /// matters if Stripe later reverses the transfer — then we flip it to 'failed' and
        /// alert super admins. Other transfer.* events are no-ops.
        /// </summary>
        private async Task HandleTransferEvent(string eventType, TransferEventInfo info)
        {
            var isReversal = eventType == "transfer.reversed" || (eventType == "transfer.updated" && info.Reversed);
            if (!isReversal) return;

            var payout = await _payouts.GetByExternalReference(info.TransferId);
            if (payout is null)
            {
                _logger.LogDebug("Stripe transfer {TransferId} reversed but doesn't match any payout row.", info.TransferId);
                return;
            }
            if (payout.Status == "failed") return;  // already handled

            await _payouts.UpdateStatus(
                id: payout.Id,
                tenantId: payout.TenantId,
                status: "failed",
                payoutDateUtc: null,
                externalReference: info.TransferId,
                memo: payout.Memo,
                approvedByUserId: payout.ApprovedByUserId);

            var amount = $"${(payout.NetPaidCents / 100m):0.00}";
            await _notifications.EmitToSuperAdmins(
                kind: "payout_failed",
                title: $"Stripe payout reversed: {amount}",
                body: $"Stripe reversed the transfer {info.TransferId} for tenant {payout.TenantId} ({amount}). The ledger entries remain attached to this payout — void it before retrying.",
                linkUrl: "/SuperAdmin",
                tenantId: payout.TenantId);
        }

        private async Task HandleDispute(DisputeInfo info)
        {
            if (string.IsNullOrEmpty(info.PaymentIntentId))
            {
                _logger.LogWarning("Dispute {DisputeId} has no payment_intent — cannot link to tenant.", info.DisputeId);
                return;
            }

            // A counter-cart PI may have many line items; back them all out on a lost dispute.
            var passes = await _passPurchases.ListByStripePaymentIntentId(info.PaymentIntentId);
            var tickets = await _ticketPurchases.ListByStripePaymentIntentId(info.PaymentIntentId);

            Guid? tenantId = passes.FirstOrDefault()?.TenantId ?? tickets.FirstOrDefault()?.TenantId;
            Guid? passId = passes.FirstOrDefault()?.Id;
            Guid? ticketId = passes.Count == 0 ? tickets.FirstOrDefault()?.Id : null;

            if (tenantId is null)
            {
                _logger.LogWarning("Dispute {DisputeId} references payment_intent {IntentId} with no matching purchase.",
                    info.DisputeId, info.PaymentIntentId);
                return;
            }

            // Detect transitions into action-required states so we only notify on first arrival /
            // status flip (not every webhook re-fire while the dispute sits at the same status).
            var existing = await _disputes.GetByStripeDisputeId(info.DisputeId);
            var newlyActionRequired =
                (info.Status == "needs_response" || info.Status == "warning_needs_response")
                && (existing is null || existing.Status != info.Status);

            await _disputes.Upsert(new Dispute
            {
                TenantId = tenantId.Value,
                PassPurchaseId = passId,
                EventTicketPurchaseId = ticketId,
                StripeDisputeId = info.DisputeId,
                StripePaymentIntentId = info.PaymentIntentId,
                StripeChargeId = info.ChargeId,
                AmountCents = info.AmountCents,
                Currency = info.Currency,
                Reason = info.Reason,
                Status = info.Status,
                EvidenceDueBy = info.EvidenceDueBy,
                StripeCreatedAt = info.StripeCreatedAt,
            });

            if (newlyActionRequired)
            {
                var amountStr = $"${(info.AmountCents / 100m):0.00} {info.Currency.ToUpper()}";
                var due = info.EvidenceDueBy.HasValue
                    ? $" Evidence due {info.EvidenceDueBy.Value:yyyy-MM-dd}."
                    : "";
                await _notifications.EmitToSuperAdmins(
                    kind: "dispute_opened",
                    title: $"Dispute filed: {amountStr}",
                    body: $"A new dispute on payment_intent {info.PaymentIntentId} needs response.{due}",
                    linkUrl: "/SuperAdmin",
                    tenantId: tenantId);
                await _notifications.EmitToTenantAdmins(
                    tenantId: tenantId.Value,
                    kind: "dispute_opened",
                    title: $"Dispute filed: {amountStr}",
                    body: $"A customer disputed a charge.{due} The platform will respond on your behalf.",
                    linkUrl: "/Admin/Purchases");
            }

            // Lost dispute = chargeback. Back out each line item with a negative ledger entry
            // mirroring the original sale, so tenant balance + lifetime totals stay correct.
            if (info.Status == "lost")
            {
                foreach (var dp in passes)
                {
                    await WriteDisputeLossEntry(dp.TenantId, "pass", dp.Id, info.DisputeId);
                }
                foreach (var t in tickets)
                {
                    await WriteDisputeLossEntry(t.TenantId, "event_ticket", t.Id, info.DisputeId);
                }

                // Notify super admins (in-app + email) and the tenant admin team (in-app) about the chargeback.
                var amountStr = $"${(info.AmountCents / 100m):0.00} {info.Currency.ToUpper()}";
                await _notifications.EmitToSuperAdmins(
                    kind: "dispute_lost",
                    title: $"Chargeback lost: {amountStr}",
                    body: $"A dispute on payment_intent {info.PaymentIntentId} was lost. The tenant has been debited.",
                    linkUrl: "/SuperAdmin",
                    tenantId: tenantId);
                await _notifications.EmitToTenantAdmins(
                    tenantId: tenantId.Value,
                    kind: "dispute_lost",
                    title: $"Chargeback: {amountStr}",
                    body: "A customer dispute was lost. The amount has been debited from your balance.",
                    linkUrl: "/Admin/Payouts");

                // Stripe also charges a flat dispute fee per chargeback. Pass through to the tenant
                // as a single dispute_fee entry tied to the first matched source. Idempotent via
                // partial unique index on (tenant_id, source_kind, source_id) where entry_kind='dispute_fee'.
                if (_disputeFeeCents > 0)
                {
                    var firstDp = passes.FirstOrDefault();
                    var firstTicket = tickets.FirstOrDefault();
                    string? srcKind = null; Guid? srcId = null; string? piId = null;
                    if (firstDp is not null) { srcKind = "pass"; srcId = firstDp.Id; piId = firstDp.StripePaymentIntentId; }
                    else if (firstTicket is not null) { srcKind = "event_ticket"; srcId = firstTicket.Id; piId = firstTicket.StripePaymentIntentId; }
                    if (srcKind is not null && srcId is not null)
                    {
                        await WriteDisputeFeeEntry(tenantId.Value, srcKind, srcId.Value, piId, info.DisputeId);
                    }
                }
            }
        }

        private async Task WriteDisputeFeeEntry(Guid tenantId, string sourceKind, Guid sourceId, string? piId, string stripeDisputeId)
        {
            try
            {
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = tenantId,
                    EntryKind = "dispute_fee",
                    SourceKind = sourceKind,
                    SourceId = sourceId,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = 0,
                    StripeFeeCents = _disputeFeeCents,    // Stripe charged us this; lifetime stripe fee totals reflect reality
                    RidepassCutCents = 0,
                    NetToTenantCents = -_disputeFeeCents, // tenant absorbs the chargeback fee
                    StripePaymentIntentId = piId,
                    Memo = $"Dispute fee for {stripeDisputeId}",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("dispute_fee entry for {Kind} {Id} already exists; skipping.", sourceKind, sourceId);
            }
        }

        private async Task WriteDisputeLossEntry(Guid tenantId, string sourceKind, Guid sourceId, string stripeDisputeId)
        {
            var sale = await _ledger.GetSaleEntryForSource(tenantId, sourceKind, sourceId);
            if (sale is null) return;
            try
            {
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = tenantId,
                    EntryKind = "dispute_loss",
                    SourceKind = sourceKind,
                    SourceId = sourceId,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = -sale.GrossCents,
                    StripeFeeCents = -sale.StripeFeeCents,
                    RidepassCutCents = -sale.RidepassCutCents,
                    NetToTenantCents = -sale.NetToTenantCents,
                    AppliedTierId = sale.AppliedTierId,
                    StripePaymentIntentId = sale.StripePaymentIntentId,
                    Memo = $"Chargeback {stripeDisputeId}",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                // Idempotent: already wrote this dispute_loss.
                _logger.LogDebug("dispute_loss entry for {Kind} {Id} already exists; skipping.", sourceKind, sourceId);
            }
        }

    }
}
