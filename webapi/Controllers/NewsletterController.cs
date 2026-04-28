using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Newsletter;
using webapi.Multitenancy;
using System.Security.Claims;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class NewsletterController : ControllerBase
    {
        private readonly INewsletterRepository _subscribers;
        private readonly ITenantRepository _tenants;
        private readonly IUserRepository _users;
        private readonly ITenantContext _tenantContext;

        public NewsletterController(
            INewsletterRepository subscribers,
            ITenantRepository tenants,
            IUserRepository users,
            ITenantContext tenantContext)
        {
            _subscribers = subscribers;
            _tenants = tenants;
            _users = users;
            _tenantContext = tenantContext;
        }

        // Public — runs on a tenant subdomain and subscribes to that tenant's list.
        // If the email matches an existing user (tenant-scoped or global rider) and no name
        // was provided, borrow the user's name so the list entry is less anonymous.
        [AllowAnonymous]
        [HttpPost("Subscribe")]
        public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("Subscribing must happen on a tenant subdomain.");
            }
            var email = request.Email.Trim();
            var name = NormalizeString(request.Name);
            if (name is null)
            {
                var user = await _users.GetByEmail(_tenantContext.TenantId, email)
                         ?? await _users.GetGlobalByEmail(email);
                if (user is not null)
                {
                    var fullName = $"{user.FirstName} {user.LastName}".Trim();
                    if (!string.IsNullOrWhiteSpace(fullName)) name = fullName;
                }
            }
            await _subscribers.UpsertFromSignup(_tenantContext.TenantId, email, name, "signup");
            return new ApiResponses().OkResult(new { subscribed = true });
        }

        // Public — no auth. The token itself is the secret.
        [AllowAnonymous]
        [HttpGet("Unsubscribe/{token:guid}/Status")]
        public async Task<IActionResult> UnsubscribeStatus(Guid token)
        {
            var sub = await _subscribers.GetByUnsubscribeToken(token);
            if (sub is null)
            {
                return new ApiResponses().NotFoundResult("Unsubscribe link is invalid or expired.");
            }
            var tenant = await _tenants.GetById(sub.TenantId);
            return new ApiResponses().OkResult(new UnsubscribeStatusResponse
            {
                Email = sub.Email,
                Name = sub.Name,
                TenantDisplayName = tenant?.DisplayName ?? "",
                Unsubscribed = sub.UnsubscribedAt.HasValue,
            });
        }

        [AllowAnonymous]
        [HttpPost("Unsubscribe/{token:guid}")]
        public async Task<IActionResult> Unsubscribe(Guid token)
        {
            var sub = await _subscribers.GetByUnsubscribeToken(token);
            if (sub is null)
            {
                return new ApiResponses().NotFoundResult("Unsubscribe link is invalid or expired.");
            }
            if (!sub.UnsubscribedAt.HasValue)
            {
                await _subscribers.Unsubscribe(sub.Id);
            }
            return new ApiResponses().OkResult(new { unsubscribed = true });
        }

        [AllowAnonymous]
        [HttpPost("Resubscribe/{token:guid}")]
        public async Task<IActionResult> Resubscribe(Guid token)
        {
            var sub = await _subscribers.GetByUnsubscribeToken(token);
            if (sub is null)
            {
                return new ApiResponses().NotFoundResult("Link is invalid or expired.");
            }
            if (sub.UnsubscribedAt.HasValue)
            {
                await _subscribers.Resubscribe(sub.Id);
            }
            return new ApiResponses().OkResult(new { unsubscribed = false });
        }

        // ── Authenticated user (rider) ────────────────────────────────────────────

        [Authorize]
        [HttpGet("Me/Status")]
        public async Task<IActionResult> GetMyStatus()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("Newsletter subscription requires a tenant subdomain.");
            }
            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }
            var user = await _users.GetById(userId);
            if (user is null)
            {
                return new ApiResponses().NotFoundResult("User not found.");
            }
            var sub = await _subscribers.GetByEmail(_tenantContext.TenantId, user.Email);
            return new ApiResponses().OkResult(new
            {
                subscribed = sub != null && !sub.UnsubscribedAt.HasValue,
                email = user.Email,
            });
        }

        [Authorize]
        [HttpPost("Me/Subscribe")]
        public async Task<IActionResult> SubscribeMe()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("Newsletter subscription requires a tenant subdomain.");
            }
            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }
            var user = await _users.GetById(userId);
            if (user is null)
            {
                return new ApiResponses().NotFoundResult("User not found.");
            }
            var fullName = $"{user.FirstName} {user.LastName}".Trim();
            await _subscribers.UpsertFromSignup(_tenantContext.TenantId, user.Email,
                string.IsNullOrWhiteSpace(fullName) ? null : fullName, "account");
            return new ApiResponses().OkResult(new { subscribed = true });
        }

        [Authorize]
        [HttpPost("Me/Unsubscribe")]
        public async Task<IActionResult> UnsubscribeMe()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("Newsletter subscription requires a tenant subdomain.");
            }
            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }
            var user = await _users.GetById(userId);
            if (user is null)
            {
                return new ApiResponses().NotFoundResult("User not found.");
            }
            var sub = await _subscribers.GetByEmail(_tenantContext.TenantId, user.Email);
            if (sub is not null && !sub.UnsubscribedAt.HasValue)
            {
                await _subscribers.Unsubscribe(sub.Id);
            }
            return new ApiResponses().OkResult(new { subscribed = false });
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }

        // ── Tenant admin ──────────────────────────────────────────────────────────

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpGet("Admin/Subscribers")]
        public async Task<IActionResult> ListSubscribers([FromQuery] bool includeUnsubscribed = false)
        {
            var rows = await _subscribers.ListByTenant(_tenantContext.TenantId, includeUnsubscribed);
            var items = rows.Select(r => new SubscriberListItem
            {
                Id = r.Id,
                Email = r.Email,
                Name = r.Name,
                Source = r.Source,
                SubscribedAtUtc = DateTime.SpecifyKind(r.SubscribedAt, DateTimeKind.Utc),
                UnsubscribedAtUtc = r.UnsubscribedAt.HasValue
                    ? DateTime.SpecifyKind(r.UnsubscribedAt.Value, DateTimeKind.Utc) : null,
            });
            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPost("Admin/Subscribers")]
        public async Task<IActionResult> AddSubscriber([FromBody] AdminAddSubscriberRequest request)
        {
            var email = request.Email.Trim();
            await _subscribers.UpsertFromSignup(_tenantContext.TenantId, email, NormalizeString(request.Name), "admin");
            return new ApiResponses().OkResult(new { added = true });
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpPost("Admin/Subscribers/Import")]
        public async Task<IActionResult> ImportSubscribers([FromBody] ImportSubscribersRequest request)
        {
            int added = 0, reactivated = 0, skipped = 0;
            var lines = (request.RawLines ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(',', 2, StringSplitOptions.TrimEntries);
                var email = parts[0];
                if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                {
                    skipped++;
                    continue;
                }
                var name = parts.Length > 1 ? NormalizeString(parts[1]) : null;
                var existing = await _subscribers.GetByEmail(_tenantContext.TenantId, email);
                var wasUnsub = existing?.UnsubscribedAt.HasValue == true;
                await _subscribers.UpsertFromSignup(_tenantContext.TenantId, email, name, "import");
                if (existing is null) added++;
                else if (wasUnsub) reactivated++;
                else skipped++;
            }
            return new ApiResponses().OkResult(new ImportSubscribersResponse
            {
                Added = added,
                Reactivated = reactivated,
                Skipped = skipped,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpDelete("Admin/Subscribers/{id:guid}")]
        public async Task<IActionResult> DeleteSubscriber(Guid id)
        {
            await _subscribers.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { deleted = true });
        }

        [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
        [HttpGet("Admin/ActiveCount")]
        public async Task<IActionResult> GetActiveCount()
        {
            var count = await _subscribers.CountActive(_tenantContext.TenantId);
            return new ApiResponses().OkResult(new { count });
        }

        private static string? NormalizeString(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
