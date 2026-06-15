using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Services.Email;

namespace webapi.Controllers
{
    /// <summary>
    /// Receives Amazon SES bounce/complaint events via SNS and feeds them into the suppression
    /// list. Server-to-server: no auth, the SNS signature is the trust anchor (verified in the
    /// service). Disabled by default so it can ship dark until the SES topic is wired up.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SesWebhookController : ControllerBase
    {
        private readonly ISesNotificationService _ses;
        private readonly IConfiguration _config;

        public SesWebhookController(ISesNotificationService ses, IConfiguration config)
        {
            _ses = ses;
            _config = config;
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            // Ship dark: until the SES/SNS topic is configured, behave as if the route isn't here.
            if (!_config.GetValue("Email:Ses:WebhookEnabled", false))
            {
                return NotFound();
            }

            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            var result = await _ses.HandleAsync(rawBody);
            return result switch
            {
                SesHandleResult.Handled => Ok(),
                SesHandleResult.BadSignature => StatusCode(403),
                _ => BadRequest(),
            };
        }
    }
}
