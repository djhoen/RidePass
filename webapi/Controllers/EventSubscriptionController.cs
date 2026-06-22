using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.EventData;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.Event;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventSubscriptionController : ControllerBase
    {
        private readonly IEventSubscriptionRepository _subs;
        private readonly ITenantRepository _tenants;
        private readonly IUserRepository _users;
        private readonly ITenantContext _tenantContext;

        public EventSubscriptionController(
            IEventSubscriptionRepository subs,
            ITenantRepository tenants,
            IUserRepository users,
            ITenantContext tenantContext)
        {
            _subs = subs;
            _tenants = tenants;
            _users = users;
            _tenantContext = tenantContext;
        }

        /// <summary>
        /// Public subscribe: anyone can leave their email (and optionally phone) to be notified
        /// when this tenant publishes new events. Idempotent — re-subscribing updates channel
        /// preferences and clears any prior unsubscribed_at flag.
        /// </summary>
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Subscribe([FromBody] SubscribeEventsRequest request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }
            if (!_tenantContext.Tenant.AllowEventSubscriptions)
            {
                return new ApiResponses().BadRequestResult("This track isn't accepting event subscriptions right now.");
            }
            if (!request.NotifyEmail && !request.NotifySms)
            {
                return new ApiResponses().BadRequestResult("Pick at least one notification channel.");
            }

            string? phone = null;
            if (request.NotifySms)
            {
                phone = TwilioSmsSender.NormalizeE164(request.Phone ?? string.Empty);
                if (phone is null)
                {
                    return new ApiResponses().BadRequestResult("A valid phone number is required for SMS notifications.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.Phone))
            {
                // Carry the value but don't gate on validity if SMS isn't selected.
                phone = TwilioSmsSender.NormalizeE164(request.Phone) ?? request.Phone.Trim();
            }

            Guid? userId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                var claim = User.FindFirst("UserId")?.Value;
                if (Guid.TryParse(claim, out var uid)) userId = uid;
            }

            var sub = new EventSubscription
            {
                TenantId = _tenantContext.TenantId,
                UserId = userId,
                Email = request.Email.Trim(),
                Phone = phone,
                NotifyEmail = request.NotifyEmail,
                NotifySms = request.NotifySms,
            };
            await _subs.Upsert(sub);
            return new ApiResponses().OkResult();
        }

        [AllowAnonymous]
        [HttpGet("Status")]
        public async Task<IActionResult> StatusByEmail([FromQuery] string email)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }
            var sub = await _subs.GetByTenantAndEmail(_tenantContext.TenantId, email.Trim());
            return new ApiResponses().OkResult(new EventSubscriptionStatusResponse
            {
                Subscribed = sub is not null && sub.UnsubscribedAt is null,
                Email = sub?.Email,
                Phone = sub?.Phone,
                NotifyEmail = sub?.NotifyEmail ?? false,
                NotifySms = sub?.NotifySms ?? false,
                TenantDisplayName = _tenantContext.Tenant.DisplayName,
            });
        }

        [Authorize]
        [HttpGet("Mine")]
        public async Task<IActionResult> Mine()
        {
            var claim = User.FindFirst("UserId")?.Value;
            if (!Guid.TryParse(claim, out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }
            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().NotFoundResult("User not found.");
            var sub = await _subs.GetByTenantAndEmail(_tenantContext.TenantId, user.Email);
            return new ApiResponses().OkResult(new EventSubscriptionStatusResponse
            {
                Subscribed = sub is not null && sub.UnsubscribedAt is null,
                Email = sub?.Email ?? user.Email,
                Phone = sub?.Phone,
                NotifyEmail = sub?.NotifyEmail ?? false,
                NotifySms = sub?.NotifySms ?? false,
                TenantDisplayName = _tenantContext.Tenant.DisplayName,
            });
        }

        /// <summary>
        /// Authenticated rider updating their own new-event notification channels from the profile.
        /// Turning a channel on requires the tenant to allow subscriptions; turning everything off
        /// (unsubscribe) is always permitted so a rider can opt out even if the track later disabled it.
        /// </summary>
        [Authorize]
        [HttpPut("Mine")]
        public async Task<IActionResult> UpdateMine([FromBody] UpdateMyEventSubscriptionRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var claim = User.FindFirst("UserId")?.Value;
            if (!Guid.TryParse(claim, out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().NotFoundResult("User not found.");

            var existing = await _subs.GetByTenantAndEmail(_tenantContext.TenantId, user.Email);

            // Both channels off => unsubscribe (idempotent).
            if (!request.NotifyEmail && !request.NotifySms)
            {
                if (existing is not null && existing.UnsubscribedAt is null)
                {
                    await _subs.SetUnsubscribed(existing.Id, true);
                }
                return new ApiResponses().OkResult(new { subscribed = false });
            }

            if (!_tenantContext.Tenant.AllowEventSubscriptions)
            {
                return new ApiResponses().BadRequestResult("This track isn't accepting event subscriptions right now.");
            }

            string? phone;
            if (request.NotifySms)
            {
                phone = TwilioSmsSender.NormalizeE164(user.Phone ?? string.Empty);
                if (phone is null)
                {
                    return new ApiResponses().BadRequestResult(
                        "Add a valid mobile phone to your profile to get text notifications.");
                }
            }
            else
            {
                // Keep any phone already on the subscription; email-only is fine.
                phone = existing?.Phone;
            }

            await _subs.Upsert(new EventSubscription
            {
                TenantId = _tenantContext.TenantId,
                UserId = userId,
                Email = user.Email,
                Phone = phone,
                NotifyEmail = request.NotifyEmail,
                NotifySms = request.NotifySms,
            });
            return new ApiResponses().OkResult(new { subscribed = true });
        }

        [AllowAnonymous]
        [HttpGet("Unsubscribe/{token:guid}/Status")]
        public async Task<IActionResult> UnsubscribeStatus(Guid token)
        {
            var sub = await _subs.GetByUnsubscribeToken(token);
            if (sub is null) return new ApiResponses().NotFoundResult("Unsubscribe link is invalid or expired.");
            var tenant = await _tenants.GetById(sub.TenantId);
            return new ApiResponses().OkResult(new EventSubscriptionStatusResponse
            {
                Subscribed = sub.UnsubscribedAt is null,
                Email = sub.Email,
                Phone = sub.Phone,
                NotifyEmail = sub.NotifyEmail,
                NotifySms = sub.NotifySms,
                TenantDisplayName = tenant?.DisplayName ?? string.Empty,
            });
        }

        [AllowAnonymous]
        [HttpPost("Unsubscribe/{token:guid}")]
        public async Task<IActionResult> Unsubscribe(Guid token)
        {
            var sub = await _subs.GetByUnsubscribeToken(token);
            if (sub is null) return new ApiResponses().NotFoundResult("Unsubscribe link is invalid or expired.");
            await _subs.SetUnsubscribed(sub.Id, true);
            return new ApiResponses().OkResult();
        }

        [AllowAnonymous]
        [HttpPost("Resubscribe/{token:guid}")]
        public async Task<IActionResult> Resubscribe(Guid token)
        {
            var sub = await _subs.GetByUnsubscribeToken(token);
            if (sub is null) return new ApiResponses().NotFoundResult("Link is invalid or expired.");
            await _subs.SetUnsubscribed(sub.Id, false);
            return new ApiResponses().OkResult();
        }
    }
}
