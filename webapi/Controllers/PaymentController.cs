using Microsoft.AspNetCore.Mvc;
using Services.Notifications;
using Services.Payments;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.Payment;
using webapi.Payments;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentProvider _payments;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IDisputeRepository _disputes;
        private readonly ITenantLedgerRepository _ledger;
        private readonly INotificationService _notifications;
        private readonly ITenantRepository _tenants;
        private readonly ITenantPayoutRepository _payouts;
        private readonly IConfiguration _config;
        private readonly IStripePurchaseFinalizer _finalizer;
        private readonly ILogger<PaymentController> _logger;
        private readonly int _disputeFeeCents;

        public PaymentController(
            IPaymentProvider payments,
            IEventTicketPurchaseRepository ticketPurchases,
            IDisputeRepository disputes,
            ITenantLedgerRepository ledger,
            INotificationService notifications,
            ITenantRepository tenants,
            ITenantPayoutRepository payouts,
            IStripePurchaseFinalizer finalizer,
            IConfiguration configuration,
            ILogger<PaymentController> logger)
        {
            _payments = payments;
            _ticketPurchases = ticketPurchases;
            _disputes = disputes;
            _ledger = ledger;
            _notifications = notifications;
            _tenants = tenants;
            _payouts = payouts;
            _finalizer = finalizer;
            _config = configuration;
            _logger = logger;
            // Stripe charges $15 USD per lost dispute (default). Override per-deploy via Stripe:DisputeFeeCents.
            _disputeFeeCents = configuration.GetValue<int?>("Stripe:DisputeFeeCents") ?? 1500;
        }

        // Platform-account webhook: events for charges on RidePass's own account ('platform' mode).
        [HttpPost("Webhook")]
        public Task<IActionResult> StripeWebhook() => HandleWebhook(connect: false);

        // Connect webhook: events for direct charges on tenants' own connected accounts ('direct'
        // mode). Signed with a separate secret; the event's `account` is the connected account id.
        [HttpPost("ConnectWebhook")]
        public Task<IActionResult> StripeConnectWebhook() => HandleWebhook(connect: true);

        private async Task<IActionResult> HandleWebhook(bool connect)
        {
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            var signature = Request.Headers["Stripe-Signature"].ToString();
            var webhookEvent = _payments.VerifyAndParseWebhook(rawBody, signature, connect);
            if (webhookEvent is null)
            {
                return BadRequest();
            }

            if (webhookEvent.Dispute is not null)
            {
                await HandleDispute(webhookEvent.Dispute, webhookEvent.ConnectedAccountId);
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

            await _finalizer.ProcessPaymentIntentAsync(webhookEvent.PaymentIntentId, webhookEvent.Type);
            return Ok();
        }

        // Client-confirm fallback. When the buyer's payment succeeds inline in the browser
        // we finalize right away instead of waiting for the async webhook (or the reconciler's
        // 20-minute grace), so the just-bought entry appears on their schedule immediately.
        // We re-read the status from Stripe rather than trusting the client, and the finalizer
        // is idempotent, so racing the real webhook is safe. Anonymous: guests check out too.
        [HttpPost("ConfirmIntent")]
        public async Task<IActionResult> ConfirmIntent([FromBody] ConfirmIntentRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.PaymentIntentId))
            {
                return BadRequest();
            }

            var status = await _payments.GetPaymentIntentStatusAsync(request.PaymentIntentId);
            if (status == "succeeded")
            {
                await _finalizer.ProcessPaymentIntentAsync(request.PaymentIntentId, "payment_intent.succeeded");
            }
            return Ok(new { status });
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

        private async Task HandleDispute(DisputeInfo info, string? connectedAccountId)
        {
            if (string.IsNullOrEmpty(info.PaymentIntentId))
            {
                _logger.LogWarning("Dispute {DisputeId} has no payment_intent — cannot link to tenant.", info.DisputeId);
                return;
            }

            // A counter-cart PI may have many line items; back them all out on a lost dispute.
            var tickets = await _ticketPurchases.ListByStripePaymentIntentId(info.PaymentIntentId);

            // Direct charge: the dispute (and its fee) hit the tenant's own Stripe account, not our
            // platform balance, so there is nothing to claw back through our ledger. Determined from
            // the purchase snapshot (authoritative regardless of which webhook endpoint delivered it),
            // falling back to the connected-account id on the event.
            var isDirect = tickets.Any(t => !string.IsNullOrEmpty(t.StripeConnectedAccountId))
                || !string.IsNullOrEmpty(connectedAccountId);

            Guid? tenantId = tickets.FirstOrDefault()?.TenantId;
            Guid? ticketId = tickets.FirstOrDefault()?.Id;

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
                var amountStr = $"${(info.AmountCents / 100m):0.00} {info.Currency.ToUpper()}";

                if (isDirect)
                {
                    // The chargeback + Stripe's dispute fee already came out of the tenant's own
                    // Stripe balance. We owe them nothing and they owe us nothing here, so write no
                    // ledger entries — just inform both sides.
                    await _notifications.EmitToSuperAdmins(
                        kind: "dispute_lost",
                        title: $"Chargeback lost (direct): {amountStr}",
                        body: $"A dispute on payment_intent {info.PaymentIntentId} was lost on the tenant's own Stripe account. No platform ledger impact.",
                        linkUrl: "/SuperAdmin",
                        tenantId: tenantId);
                    await _notifications.EmitToTenantAdmins(
                        tenantId: tenantId.Value,
                        kind: "dispute_lost",
                        title: $"Chargeback: {amountStr}",
                        body: "A customer dispute was lost. The amount and Stripe's dispute fee were deducted from your Stripe balance.",
                        linkUrl: "/Admin/Purchases");
                    return;
                }

                foreach (var t in tickets)
                {
                    await WriteDisputeLossEntry(t.TenantId, "event_ticket", t.Id, info.DisputeId);
                }

                // Notify super admins (in-app + email) and the tenant admin team (in-app) about the chargeback.
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
                    var firstTicket = tickets.FirstOrDefault();
                    if (firstTicket is not null)
                    {
                        await WriteDisputeFeeEntry(tenantId.Value, "event_ticket", firstTicket.Id, firstTicket.StripePaymentIntentId, info.DisputeId);
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
