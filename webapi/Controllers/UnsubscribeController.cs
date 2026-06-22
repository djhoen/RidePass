using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Interfaces;

namespace webapi.Controllers
{
    /// <summary>
    /// Provider-agnostic one-click unsubscribe (RFC 8058). The token in the link carries the
    /// tenant + recipient, so this endpoint needs no auth and no tenant resolution: the token
    /// is the secret. Writes a 'marketing'-scope suppression so transactional mail (receipts,
    /// verification) still reaches the address.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class UnsubscribeController : ControllerBase
    {
        private readonly IEmailSuppressionRepository _suppression;
        private readonly IEmailLinkTokens _tokens;
        private readonly ITenantRepository _tenants;

        public UnsubscribeController(
            IEmailSuppressionRepository suppression,
            IEmailLinkTokens tokens,
            ITenantRepository tenants)
        {
            _suppression = suppression;
            _tokens = tokens;
            _tenants = tenants;
        }

        // RFC 8058 one-click: the mail client POSTs here (body "List-Unsubscribe=One-Click")
        // with the token in the query string. No interstitial, no auth. Always tenant-scoped:
        // the mail client calls a fixed URL, so the "stop everything" choice lives on the page.
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> OneClick([FromQuery] string token)
        {
            if (!_tokens.TryParseUnsubscribe(token, out var tenantId, out var email))
            {
                return new ApiResponses().BadRequestResult("Unsubscribe link is invalid.");
            }
            await _suppression.Suppress(tenantId, email, "unsubscribe", "marketing", "one_click", null);
            return new ApiResponses().OkResult(new { unsubscribed = true });
        }

        // Page-driven "stop promotional emails from every track on the platform". Writes a
        // platform-wide (tenant_id NULL) marketing suppression, so it blocks marketing for this
        // address across all tenants while leaving transactional mail (receipts) untouched.
        [AllowAnonymous]
        [HttpPost("AllTracks")]
        public async Task<IActionResult> AllTracks([FromQuery] string token)
        {
            if (!_tokens.TryParseUnsubscribe(token, out _, out var email))
            {
                return new ApiResponses().BadRequestResult("Unsubscribe link is invalid.");
            }
            await _suppression.Suppress(null, email, "unsubscribe", "marketing", "one_click_all", null);
            return new ApiResponses().OkResult(new { unsubscribed = true, scope = "all_tracks" });
        }

        // Public resubscribe from the confirmation page ("changed your mind?"). Clears the
        // tenant-scoped marketing suppression the token's address opted into. The platform-wide
        // "all tracks" opt-out is intentionally not undone here (it's a separate, broader choice).
        [AllowAnonymous]
        [HttpPost("Resubscribe")]
        public async Task<IActionResult> Resubscribe([FromQuery] string token)
        {
            if (!_tokens.TryParseUnsubscribe(token, out var tenantId, out var email))
            {
                return new ApiResponses().BadRequestResult("Unsubscribe link is invalid.");
            }
            await _suppression.Unsuppress(tenantId, email, "marketing");
            return new ApiResponses().OkResult(new { unsubscribed = false });
        }

        // Backing data for a visible confirmation page reached from the body link.
        [AllowAnonymous]
        [HttpGet("Status")]
        public async Task<IActionResult> Status([FromQuery] string token)
        {
            if (!_tokens.TryParseUnsubscribe(token, out var tenantId, out var email))
            {
                return new ApiResponses().BadRequestResult("Unsubscribe link is invalid.");
            }
            var alreadyOff = await _suppression.IsSuppressed(email, tenantId, marketing: true);
            string tenantName = "";
            if (tenantId.HasValue)
            {
                var tenant = await _tenants.GetById(tenantId.Value);
                tenantName = tenant?.DisplayName ?? "";
            }
            return new ApiResponses().OkResult(new
            {
                email,
                tenantDisplayName = tenantName,
                unsubscribed = alreadyOff,
            });
        }
    }
}
