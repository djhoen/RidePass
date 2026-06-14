using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Newsletter;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
    public class CampaignController : ControllerBase
    {
        // Bulk email delivery is not built yet. While this is false, the Send action is a
        // hard no-op: it does not change the campaign to a "sent" state and does not write
        // fake send rows, so the UI can never claim a campaign went out during client testing.
        // Flip to true (and finish the SMTP/SES wiring in Send) to re-enable delivery.
        private const bool DeliveryEnabled = false;

        private readonly IEmailCampaignRepository _campaigns;
        private readonly INewsletterRepository _subscribers;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<CampaignController> _logger;

        public CampaignController(
            IEmailCampaignRepository campaigns,
            INewsletterRepository subscribers,
            ITenantContext tenantContext,
            ILogger<CampaignController> logger)
        {
            _campaigns = campaigns;
            _subscribers = subscribers;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var rows = await _campaigns.ListByTenant(_tenantContext.TenantId);
            var items = rows.Select(ToListItem);
            return new ApiResponses().OkResult(items);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var c = await _campaigns.GetById(id, _tenantContext.TenantId);
            if (c is null)
            {
                return new ApiResponses().NotFoundResult("Campaign not found.");
            }
            return new ApiResponses().OkResult(ToDetail(c));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertCampaignRequest request)
        {
            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }
            var c = new EmailCampaign
            {
                TenantId = _tenantContext.TenantId,
                Subject = request.Subject.Trim(),
                BodyHtml = request.BodyHtml,
                BodyText = request.BodyText,
                Status = "draft",
                CreatedByUserId = userId,
            };
            c.Id = await _campaigns.Create(c);
            return new ApiResponses().OkResult(ToDetail(c));
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertCampaignRequest request)
        {
            var existing = await _campaigns.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Campaign not found.");
            }
            if (existing.Status != "draft")
            {
                return new ApiResponses().BadRequestResult("Only draft campaigns can be edited.");
            }
            existing.Subject = request.Subject.Trim();
            existing.BodyHtml = request.BodyHtml;
            existing.BodyText = request.BodyText;
            await _campaigns.Update(existing);
            return new ApiResponses().OkResult(ToDetail(existing));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _campaigns.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Campaign not found.");
            }
            if (existing.Status == "sent" || existing.Status == "sending")
            {
                return new ApiResponses().BadRequestResult("Cannot delete a campaign that has been sent.");
            }
            await _campaigns.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { deleted = true });
        }

        /// <summary>
        /// Send action. Real bulk delivery is out of scope right now, so while
        /// <see cref="DeliveryEnabled"/> is false this short-circuits to a clear no-op:
        /// it does NOT mark the campaign "sent" and does NOT write per-recipient send rows.
        /// The recipient-materialization helper code below is kept (behind the guard) so
        /// delivery can be re-enabled later by flipping the const and finishing the wiring.
        /// </summary>
        [HttpPost("{id:guid}/Send")]
        public async Task<IActionResult> Send(Guid id)
        {
            // Guard: never let an operator "send" while delivery is unbuilt. Returning a
            // BadRequest here means the campaign stays in its current (draft) state and the
            // UI cannot falsely show it as sent.
            if (!DeliveryEnabled)
            {
                return new ApiResponses().BadRequestResult("Email campaign delivery isn't enabled yet.");
            }

            var campaign = await _campaigns.GetById(id, _tenantContext.TenantId);
            if (campaign is null)
            {
                return new ApiResponses().NotFoundResult("Campaign not found.");
            }
            if (campaign.Status != "draft")
            {
                return new ApiResponses().BadRequestResult($"Cannot send a campaign with status '{campaign.Status}'.");
            }

            var recipients = await _subscribers.ListActiveForSend(_tenantContext.TenantId);
            if (recipients.Count == 0)
            {
                return new ApiResponses().BadRequestResult("No active subscribers to send to.");
            }

            await _campaigns.MarkSending(id);

            var sendRows = recipients.Select(r => new EmailCampaignSend
            {
                SubscriberId = r.Id,
                Email = r.Email,
                Name = r.Name,
                Status = "pending",
            });
            await _campaigns.CreateSendRows(id, sendRows);

            // TODO: deliver via SMTP/SES here before marking sent.
            _logger.LogInformation(
                "Campaign {CampaignId} delivering subject '{Subject}' to {Count} subscribers for tenant {TenantId}",
                id, campaign.Subject, recipients.Count, _tenantContext.TenantId);

            await _campaigns.MarkSent(id, recipients.Count);

            return new ApiResponses().OkResult(new SendCampaignResponse
            {
                CampaignId = id,
                RecipientCount = recipients.Count,
                Status = "sent",
                SendNotice = null,
            });
        }

        private static CampaignListItem ToListItem(EmailCampaign c) => new()
        {
            Id = c.Id,
            Subject = c.Subject,
            Status = c.Status,
            RecipientCount = c.RecipientCount,
            SentAtUtc = c.SentAt.HasValue ? DateTime.SpecifyKind(c.SentAt.Value, DateTimeKind.Utc) : null,
            CreatedAtUtc = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
        };

        private static CampaignDetail ToDetail(EmailCampaign c) => new()
        {
            Id = c.Id,
            Subject = c.Subject,
            Status = c.Status,
            RecipientCount = c.RecipientCount,
            SentAtUtc = c.SentAt.HasValue ? DateTime.SpecifyKind(c.SentAt.Value, DateTimeKind.Utc) : null,
            CreatedAtUtc = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
            BodyHtml = c.BodyHtml,
            BodyText = c.BodyText,
        };

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }
    }
}
