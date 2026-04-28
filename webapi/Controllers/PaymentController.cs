using Microsoft.AspNetCore.Mvc;
using Services.Payments;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaymentProvider _payments;
        private readonly IDayPassPurchaseRepository _dayPassPurchases;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IDisputeRepository _disputes;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            IPaymentProvider payments,
            IDayPassPurchaseRepository dayPassPurchases,
            IEventTicketPurchaseRepository ticketPurchases,
            IDisputeRepository disputes,
            ILogger<PaymentController> logger)
        {
            _payments = payments;
            _dayPassPurchases = dayPassPurchases;
            _ticketPurchases = ticketPurchases;
            _disputes = disputes;
            _logger = logger;
        }

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

            var dayPass = await _dayPassPurchases.GetByStripePaymentIntentId(webhookEvent.PaymentIntentId);
            if (dayPass is not null)
            {
                ApplyStatusTransition(
                    webhookEvent.Type,
                    dayPass.Status,
                    paid: () => _dayPassPurchases.UpdateStatus(dayPass.Id, "paid"),
                    failed: () => _dayPassPurchases.UpdateStatus(dayPass.Id, "failed"));
                return Ok();
            }

            var ticket = await _ticketPurchases.GetByStripePaymentIntentId(webhookEvent.PaymentIntentId);
            if (ticket is not null)
            {
                ApplyStatusTransition(
                    webhookEvent.Type,
                    ticket.Status,
                    paid: () => _ticketPurchases.UpdateStatus(ticket.Id, "paid"),
                    failed: () => _ticketPurchases.UpdateStatus(ticket.Id, "failed"));
                return Ok();
            }

            _logger.LogWarning("Received Stripe event {EventType} for unknown payment_intent {IntentId}",
                webhookEvent.Type, webhookEvent.PaymentIntentId);
            return Ok();
        }

        private async Task HandleDispute(DisputeInfo info)
        {
            if (string.IsNullOrEmpty(info.PaymentIntentId))
            {
                _logger.LogWarning("Dispute {DisputeId} has no payment_intent — cannot link to tenant.", info.DisputeId);
                return;
            }

            Guid? tenantId = null;
            Guid? dayPassId = null;
            Guid? ticketId = null;

            var dayPass = await _dayPassPurchases.GetByStripePaymentIntentId(info.PaymentIntentId);
            if (dayPass is not null)
            {
                tenantId = dayPass.TenantId;
                dayPassId = dayPass.Id;
            }
            else
            {
                var ticket = await _ticketPurchases.GetByStripePaymentIntentId(info.PaymentIntentId);
                if (ticket is not null)
                {
                    tenantId = ticket.TenantId;
                    ticketId = ticket.Id;
                }
            }

            if (tenantId is null)
            {
                _logger.LogWarning("Dispute {DisputeId} references payment_intent {IntentId} with no matching purchase.",
                    info.DisputeId, info.PaymentIntentId);
                return;
            }

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
        }

        private static void ApplyStatusTransition(string eventType, string currentStatus, Func<Task> paid, Func<Task> failed)
        {
            switch (eventType)
            {
                case "payment_intent.succeeded":
                    if (currentStatus != "paid" && currentStatus != "redeemed")
                    {
                        paid().GetAwaiter().GetResult();
                    }
                    break;
                case "payment_intent.payment_failed":
                    if (currentStatus == "pending")
                    {
                        failed().GetAwaiter().GetResult();
                    }
                    break;
            }
        }
    }
}
