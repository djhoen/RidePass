using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.MessagingData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Inbox;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Admin Inbox API for two-way SMS. Lists/reads conversation threads
    /// captured by the inbound webhook (TwilioWebhookController.IncomingSms)
    /// and lets admins reply through the same TwilioSmsSender pipeline that
    /// every other outbound SMS uses — so opt-out suppression, billing, and
    /// the tenant_message persistence all stay consistent.
    ///
    /// Gated by SettingsManage because the Inbox surfaces customer PII (phone
    /// numbers + message bodies). If we add a dedicated "messaging.manage"
    /// policy later, swap the attribute here.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
    public class TenantConversationController : ControllerBase
    {
        private readonly ITenantConversationRepository _conversations;
        private readonly ITenantRepository _tenants;
        private readonly ISmsSender _sms;
        private readonly ITenantSmsOptOutRepository _optOuts;
        private readonly IUserRepository _users;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<TenantConversationController> _logger;

        public TenantConversationController(
            ITenantConversationRepository conversations,
            ITenantRepository tenants,
            ISmsSender sms,
            ITenantSmsOptOutRepository optOuts,
            IUserRepository users,
            ITenantContext tenantContext,
            ILogger<TenantConversationController> logger)
        {
            _conversations = conversations;
            _tenants = tenants;
            _sms = sms;
            _optOuts = optOuts;
            _users = users;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool includeArchived = false)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var rows = await _conversations.ListForTenantWithOptOut(
                _tenantContext.TenantId, take: 200, includeArchived: includeArchived);

            var items = rows.Select(r => new ConversationListItem
            {
                Id = r.Id,
                CustomerPhone = r.CustomerPhone,
                LastMessageAtUtc = r.LastMessageAt,
                LastInboundAtUtc = r.LastInboundAt,
                LastReadAtUtc = r.LastReadAt,
                Status = r.Status,
                Unread = r.IsUnread,
                OptedOut = r.OptedOut,
                CustomerUserId = r.CustomerUserId,
                CustomerName = r.CustomerName,
            });

            return new ApiResponses().OkResult(items);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var conversation = await _conversations.GetById(id, _tenantContext.TenantId);
            if (conversation is null)
            {
                return new ApiResponses().NotFoundResult("Conversation not found.");
            }

            var messages = await _conversations.ListForConversation(id, _tenantContext.TenantId);
            var optedOut = await _optOuts.IsOptedOut(_tenantContext.TenantId, conversation.CustomerPhone);

            // Resolve the linked customer's display name when present. Single
            // lookup — N+1 isn't a concern on the detail endpoint (one
            // conversation per request).
            string? customerName = null;
            if (conversation.CustomerUserId.HasValue)
            {
                var user = await _users.GetById(conversation.CustomerUserId.Value);
                if (user is not null)
                {
                    var joined = $"{user.FirstName} {user.LastName}".Trim();
                    customerName = string.IsNullOrWhiteSpace(joined) ? null : joined;
                }
            }

            var detail = new ConversationDetail
            {
                Id = conversation.Id,
                CustomerPhone = conversation.CustomerPhone,
                LastMessageAtUtc = conversation.LastMessageAt,
                LastInboundAtUtc = conversation.LastInboundAt,
                LastReadAtUtc = conversation.LastReadAt,
                Status = conversation.Status,
                OptedOut = optedOut,
                CustomerUserId = conversation.CustomerUserId,
                CustomerName = customerName,
                Messages = messages.Select(m => new MessageDto
                {
                    Id = m.Id,
                    Direction = m.Direction,
                    Body = m.Body,
                    Status = m.Status,
                    NumSegments = m.NumSegments,
                    ErrorCode = m.ErrorCode,
                    ErrorMessage = m.ErrorMessage,
                    CreatedAtUtc = m.CreatedAt,
                }).ToList(),
            };

            return new ApiResponses().OkResult(detail);
        }

        [HttpPost("{id:guid}/Reply")]
        public async Task<IActionResult> Reply(Guid id, [FromBody] SendReplyRequest request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }
            var body = (request.Body ?? string.Empty).Trim();
            if (body.Length == 0)
            {
                return new ApiResponses().BadRequestResult("Message body is required.");
            }

            var conversation = await _conversations.GetById(id, _tenantContext.TenantId);
            if (conversation is null)
            {
                return new ApiResponses().NotFoundResult("Conversation not found.");
            }

            // The opt-out check inside TwilioSmsSender would silently no-op on
            // an opted-out recipient, which from the admin's perspective looks
            // identical to "send failed" — surface it explicitly here instead
            // so the UI can render a clear suppression banner.
            if (await _optOuts.IsOptedOut(_tenantContext.TenantId, conversation.CustomerPhone))
            {
                return new ApiResponses().BadRequestResult(
                    "This customer has opted out of SMS. Replies are blocked until they text START.");
            }

            var tenant = await _tenants.GetById(_tenantContext.TenantId);
            if (tenant is null)
            {
                // Token resolved to a tenant id that's been deleted out from
                // under the JWT — close fail, log loudly.
                _logger.LogWarning(
                    "Reply attempted for missing tenant {TenantId} conversation {ConversationId}",
                    _tenantContext.TenantId, id);
                return new ApiResponses().BadRequestResult("Tenant not found.");
            }

            if (!_sms.IsConfiguredFor(tenant))
            {
                return new ApiResponses().BadRequestResult(
                    "SMS isn't configured for this tenant. Visit Settings → SMS to provision a number.");
            }

            // TwilioSmsSender persists the outbound row to tenant_message as a
            // side effect of a successful send. Passing the admin's user id
            // attributes the row so the Inbox can later distinguish "human
            // reply" from "system send" (waitlist promos, scheduled blasts).
            // Missing token → null attribution rather than a 400; the send
            // itself is what matters, attribution is best-effort.
            Guid? sentByUserId = TryGetUserId(out var uid) ? uid : null;
            var sent = await _sms.Send(tenant, conversation.CustomerPhone, body, sentByUserId);
            if (!sent)
            {
                return new ApiResponses().BadRequestResult(
                    "Failed to send SMS. Check the SMS settings or try again.");
            }

            return new ApiResponses().OkResult(new { sent = true });
        }

        [HttpPost("{id:guid}/MarkRead")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }
            var conversation = await _conversations.GetById(id, _tenantContext.TenantId);
            if (conversation is null)
            {
                return new ApiResponses().NotFoundResult("Conversation not found.");
            }
            await _conversations.MarkRead(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { ok = true });
        }

        [HttpPost("{id:guid}/Status")]
        public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetConversationStatusRequest request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }
            var conversation = await _conversations.GetById(id, _tenantContext.TenantId);
            if (conversation is null)
            {
                return new ApiResponses().NotFoundResult("Conversation not found.");
            }
            await _conversations.SetStatus(id, _tenantContext.TenantId, request.Status);
            return new ApiResponses().OkResult(new { status = request.Status });
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }
    }
}
