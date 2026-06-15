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
        private readonly IEmailCampaignRepository _campaigns;
        private readonly INewsletterRepository _subscribers;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ISmtpEmailer _emailer;
        private readonly IScheduledTaskRepository _scheduledTasks;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<CampaignController> _logger;

        public CampaignController(
            IEmailCampaignRepository campaigns,
            INewsletterRepository subscribers,
            IEmailSuppressionRepository suppression,
            ISmtpEmailer emailer,
            IScheduledTaskRepository scheduledTasks,
            ITenantContext tenantContext,
            ILogger<CampaignController> logger)
        {
            _campaigns = campaigns;
            _subscribers = subscribers;
            _suppression = suppression;
            _emailer = emailer;
            _scheduledTasks = scheduledTasks;
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
        /// Materializes the (suppression-filtered) recipient rows, flips the campaign to
        /// 'sending', and enqueues a background task that does the actual SMTP delivery
        /// (SendCampaignHandler). Returns immediately so a large list doesn't time out the
        /// request. Delivery is gated on SMTP being configured, so during client testing
        /// (no SES yet) this safely refuses rather than pretending to send.
        /// </summary>
        [HttpPost("{id:guid}/Send")]
        public async Task<IActionResult> Send(Guid id, [FromQuery] DateTime? scheduledForUtc)
        {
            if (!_emailer.IsConfigured)
            {
                return new ApiResponses().BadRequestResult(
                    "Email isn't configured yet. Set up the SMTP / SES credentials before sending campaigns.");
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

            // Compliance gate: drop anyone on the suppression list (hard bounces + marketing
            // opt-outs, tenant or platform-wide) before they ever become a send row. The handler
            // re-checks at send time too, in case someone opts out between now and delivery.
            var blocklist = await _suppression.ListMarketingBlocklist(_tenantContext.TenantId);
            var beforeCount = recipients.Count;
            recipients = recipients.Where(r => !blocklist.Contains(r.Email)).ToList();
            var suppressedCount = beforeCount - recipients.Count;
            if (recipients.Count == 0)
            {
                return new ApiResponses().BadRequestResult("Every subscriber is on the suppression list; nothing to send.");
            }

            // A future time (60s grace for clock skew) schedules; otherwise send now. The
            // audience is snapshotted now; the handler re-checks suppression at delivery time
            // so opt-outs between scheduling and sending are still honored.
            var runAt = scheduledForUtc?.ToUniversalTime();
            var isScheduled = runAt.HasValue && runAt.Value > DateTime.UtcNow.AddSeconds(60);

            await _campaigns.CreateSendRows(id, recipients.Select(r => new EmailCampaignSend
            {
                SubscriberId = r.Id,
                Email = r.Email,
                Name = r.Name,
                Status = "pending",
            }));
            if (isScheduled) await _campaigns.MarkScheduled(id, runAt!.Value);
            else await _campaigns.MarkSending(id);

            // Hand delivery to the background runner (now, or at the scheduled time).
            Guid? createdBy = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(
                new Services.Scheduling.Handlers.SendCampaignPayload { CampaignId = id });
            await _scheduledTasks.Enqueue(_tenantContext.TenantId, "send_campaign", payloadJson,
                isScheduled ? runAt!.Value : DateTime.UtcNow, createdBy);

            var suppressedNote = suppressedCount > 0 ? $" ({suppressedCount} suppressed skipped)" : "";
            return new ApiResponses().OkResult(new SendCampaignResponse
            {
                CampaignId = id,
                RecipientCount = recipients.Count,
                Status = isScheduled ? "scheduled" : "sending",
                SendNotice = isScheduled
                    ? $"Scheduled for {runAt!.Value:yyyy-MM-dd HH:mm} UTC, {recipients.Count} recipient{(recipients.Count == 1 ? "" : "s")}{suppressedNote}."
                    : $"Sending to {recipients.Count} subscriber{(recipients.Count == 1 ? "" : "s")} in the background{suppressedNote}.",
            });
        }

        // Cancel a scheduled campaign before it sends: cancel the pending task, drop the
        // materialized send rows, and revert to draft so it can be edited / re-sent.
        [HttpPost("{id:guid}/Unschedule")]
        public async Task<IActionResult> Unschedule(Guid id)
        {
            var campaign = await _campaigns.GetById(id, _tenantContext.TenantId);
            if (campaign is null)
            {
                return new ApiResponses().NotFoundResult("Campaign not found.");
            }
            if (campaign.Status != "scheduled")
            {
                return new ApiResponses().BadRequestResult("Only a scheduled campaign can be unscheduled.");
            }
            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            var pending = await _scheduledTasks.ListPendingForTenant(_tenantContext.TenantId, null);
            foreach (var t in pending.Where(t => t.Kind == "send_campaign"))
            {
                try
                {
                    var p = System.Text.Json.JsonSerializer.Deserialize<Services.Scheduling.Handlers.SendCampaignPayload>(
                        t.Payload, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (p?.CampaignId == id) await _scheduledTasks.Cancel(t.Id, _tenantContext.TenantId, userId);
                }
                catch { /* skip unparseable payloads */ }
            }

            await _campaigns.DeleteSendRows(id);
            await _campaigns.RevertToDraft(id);
            return new ApiResponses().OkResult(new { unscheduled = true });
        }

        private static CampaignListItem ToListItem(EmailCampaign c) => new()
        {
            Id = c.Id,
            Subject = c.Subject,
            Status = c.Status,
            RecipientCount = c.RecipientCount,
            SentAtUtc = c.SentAt.HasValue ? DateTime.SpecifyKind(c.SentAt.Value, DateTimeKind.Utc) : null,
            ScheduledForUtc = c.ScheduledFor.HasValue ? DateTime.SpecifyKind(c.ScheduledFor.Value, DateTimeKind.Utc) : null,
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
