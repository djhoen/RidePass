using Microsoft.AspNetCore.Mvc;
using Services.Notifications;
using Services.Payments;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentProvider _payments;
        private readonly IDayPassPurchaseRepository _dayPassPurchases;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IDisputeRepository _disputes;
        private readonly IFeeCalculator _feeCalculator;
        private readonly ITenantLedgerRepository _ledger;
        private readonly INotificationService _notifications;
        private readonly ILogger<PaymentController> _logger;
        private readonly int _disputeFeeCents;

        public PaymentController(
            IPaymentProvider payments,
            IDayPassPurchaseRepository dayPassPurchases,
            IEventTicketPurchaseRepository ticketPurchases,
            IDisputeRepository disputes,
            IFeeCalculator feeCalculator,
            ITenantLedgerRepository ledger,
            INotificationService notifications,
            IConfiguration configuration,
            ILogger<PaymentController> logger)
        {
            _payments = payments;
            _dayPassPurchases = dayPassPurchases;
            _ticketPurchases = ticketPurchases;
            _disputes = disputes;
            _feeCalculator = feeCalculator;
            _ledger = ledger;
            _notifications = notifications;
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

            if (webhookEvent.PaymentIntentId is null)
            {
                return Ok();
            }

            // A counter sale can attach multiple purchase rows (mixed kinds) to one PaymentIntent,
            // so iterate everything that points at this PI rather than stopping after the first match.
            var dayPasses = await _dayPassPurchases.ListByStripePaymentIntentId(webhookEvent.PaymentIntentId);
            var tickets = await _ticketPurchases.ListByStripePaymentIntentId(webhookEvent.PaymentIntentId);

            if (dayPasses.Count == 0 && tickets.Count == 0)
            {
                _logger.LogWarning("Received Stripe event {EventType} for unknown payment_intent {IntentId}",
                    webhookEvent.Type, webhookEvent.PaymentIntentId);
                return Ok();
            }

            switch (webhookEvent.Type)
            {
                case "payment_intent.succeeded":
                    await OnPaymentSucceeded(webhookEvent.PaymentIntentId, dayPasses, tickets);
                    break;
                case "payment_intent.payment_failed":
                    foreach (var dp in dayPasses.Where(p => p.Status == "pending"))
                        await _dayPassPurchases.UpdateStatus(dp.Id, "failed");
                    foreach (var t in tickets.Where(p => p.Status == "pending"))
                        await _ticketPurchases.UpdateStatus(t.Id, "failed");
                    break;
            }
            return Ok();
        }

        private async Task OnPaymentSucceeded(
            string paymentIntentId,
            List<DayPassPurchase> dayPasses,
            List<EventTicketPurchase> tickets)
        {
            var totalStripeFee = await _payments.GetActualStripeFeeCentsAsync(paymentIntentId) ?? 0;

            // Pro-rata distribute the single PI-level Stripe fee across all line items by gross.
            var lines = dayPasses
                .Where(p => p.Status != "paid" && p.Status != "redeemed")
                .Select(p => (Kind: "day_pass", Id: p.Id, TenantId: p.TenantId, Gross: p.AmountCents,
                              MarkPaid: (Func<Task>)(() => _dayPassPurchases.UpdateStatus(p.Id, "paid"))))
                .Concat(tickets
                    .Where(t => t.Status != "paid" && t.Status != "redeemed")
                    .Select(t => (Kind: "event_ticket", Id: t.Id, TenantId: t.TenantId, Gross: t.AmountCents,
                                  MarkPaid: (Func<Task>)(() => _ticketPurchases.UpdateStatus(t.Id, "paid")))))
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

                var calc = await _feeCalculator.Calculate(line.TenantId, line.Gross, stripeFeeForLine, occurredAt);

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
                    });
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
                {
                    // Idempotent: duplicate (tenant_id, source_kind, source_id) for entry_kind='sale'.
                    // Webhook fired again for an already-recorded sale — safe to ignore.
                    _logger.LogDebug("Ledger entry for {Kind} {Id} already exists; skipping.", line.Kind, line.Id);
                }
            }
        }

        private async Task HandleDispute(DisputeInfo info)
        {
            if (string.IsNullOrEmpty(info.PaymentIntentId))
            {
                _logger.LogWarning("Dispute {DisputeId} has no payment_intent — cannot link to tenant.", info.DisputeId);
                return;
            }

            // A counter-cart PI may have many line items; back them all out on a lost dispute.
            var dayPasses = await _dayPassPurchases.ListByStripePaymentIntentId(info.PaymentIntentId);
            var tickets = await _ticketPurchases.ListByStripePaymentIntentId(info.PaymentIntentId);

            Guid? tenantId = dayPasses.FirstOrDefault()?.TenantId ?? tickets.FirstOrDefault()?.TenantId;
            Guid? dayPassId = dayPasses.FirstOrDefault()?.Id;
            Guid? ticketId = dayPasses.Count == 0 ? tickets.FirstOrDefault()?.Id : null;

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
                DayPassPurchaseId = dayPassId,
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
                foreach (var dp in dayPasses)
                {
                    await WriteDisputeLossEntry(dp.TenantId, "day_pass", dp.Id, info.DisputeId);
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
                    var firstDp = dayPasses.FirstOrDefault();
                    var firstTicket = tickets.FirstOrDefault();
                    string? srcKind = null; Guid? srcId = null; string? piId = null;
                    if (firstDp is not null) { srcKind = "day_pass"; srcId = firstDp.Id; piId = firstDp.StripePaymentIntentId; }
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
