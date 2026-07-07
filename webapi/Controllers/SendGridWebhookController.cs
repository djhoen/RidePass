using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Services.Email;

namespace webapi.Controllers
{
    /// <summary>
    /// Receives SendGrid Event Webhook deliveries (bounces, spam reports, unsubscribes) and feeds
    /// them into the suppression list. Server-to-server: no auth, the signed-event-webhook ECDSA
    /// signature is the trust anchor (verified in the service). Disabled by default so it can ship
    /// dark until the SendGrid account + verification key are configured.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SendGridWebhookController : ControllerBase
    {
        private readonly ISendGridEventService _sendgrid;
        private readonly IConfiguration _config;

        public SendGridWebhookController(ISendGridEventService sendgrid, IConfiguration config)
        {
            _sendgrid = sendgrid;
            _config = config;
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            // Ship dark: until the SendGrid account is wired up, behave as if the route isn't here.
            if (!_config.GetValue("Email:SendGrid:WebhookEnabled", false))
            {
                return NotFound();
            }

            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            var signature = Request.Headers["X-Twilio-Email-Event-Webhook-Signature"].FirstOrDefault();
            var timestamp = Request.Headers["X-Twilio-Email-Event-Webhook-Timestamp"].FirstOrDefault();

            var result = await _sendgrid.HandleAsync(rawBody, signature, timestamp);
            return result switch
            {
                SendGridHandleResult.Handled => Ok(),
                SendGridHandleResult.BadSignature => StatusCode(403),
                _ => BadRequest(),
            };
        }
    }
}
