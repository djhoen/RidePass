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
        private readonly ICampaignAudienceRepository _audiences;
        private readonly Services.Storage.IImageStorage _imageStorage;
        private readonly ITenantBrandingRepository _brandings;
        private readonly IConfiguration _config;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ISmtpEmailer _emailer;
        private readonly IScheduledTaskRepository _scheduledTasks;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<CampaignController> _logger;

        public CampaignController(
            IEmailCampaignRepository campaigns,
            INewsletterRepository subscribers,
            ICampaignAudienceRepository audiences,
            Services.Storage.IImageStorage imageStorage,
            ITenantBrandingRepository brandings,
            IConfiguration config,
            IEmailSuppressionRepository suppression,
            ISmtpEmailer emailer,
            IScheduledTaskRepository scheduledTasks,
            ITenantContext tenantContext,
            ILogger<CampaignController> logger)
        {
            _campaigns = campaigns;
            _subscribers = subscribers;
            _audiences = audiences;
            _imageStorage = imageStorage;
            _brandings = brandings;
            _config = config;
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
            // One label lookup per distinct audience, not per row.
            var labels = new Dictionary<string, string>();
            var items = new List<CampaignListItem>();
            foreach (var c in rows)
            {
                var key = c.AudienceKind + "|" + c.AudienceConfig;
                if (!labels.TryGetValue(key, out var label))
                {
                    label = await LabelFor(c);
                    labels[key] = label;
                }
                items.Add(ToListItem(c, label));
            }
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
            return new ApiResponses().OkResult(ToDetail(c, await LabelFor(c)));
        }

        /// <summary>
        /// Inline image for a campaign or automation body. Same rules as the blog and page
        /// editors; stored under the tenant's "marketing" folder. The editor inserts the returned
        /// URL, and EmailHtml makes it absolute and size-capped at send time.
        /// </summary>
        [HttpPost("Image")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            if (file is null || file.Length == 0) return new ApiResponses().BadRequestResult("Choose an image to upload.");
            if (file.Length > 5 * 1024 * 1024) return new ApiResponses().BadRequestResult("Images must be 5 MB or smaller.");
            var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
                ["image/gif"] = ".gif",
            };
            if (!allowed.TryGetValue(file.ContentType, out var ext))
            {
                return new ApiResponses().BadRequestResult("Use a PNG, JPEG, WebP, or GIF image.");
            }
            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "marketing", ext, ct);
            return new ApiResponses().OkResult(new { imageUrl = url });
        }

        /// <summary>
        /// The email exactly as it would be sent (branded header and footer, buttons, capped
        /// images, preheader), for the phone/desktop preview in the composer. With a trigger kind
        /// the merge fields are filled with that trigger's sample values.
        /// </summary>
        [HttpPost("Preview")]
        public async Task<IActionResult> Preview([FromBody] CampaignPreviewRequest request)
        {
            var tenant = _tenantContext.Tenant;
            if (tenant is null) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rootDomain = _config["Tenant:RootDomain"] ?? _config["App:RootDomain"] ?? "ridepass.io";
            var baseUrl = $"https://{tenant.Subdomain}.{rootDomain}";
            var brand = Services.Email.EmailBranding.From(tenant, await _brandings.GetByTenantId(tenant.Id), baseUrl);

            var body = request.BodyHtml ?? string.Empty;
            var preview = request.PreviewText;
            if (Services.Email.AutomationTriggers.IsKind(request.TriggerKind))
            {
                var values = Services.Email.AutomationMergeFields.Sample(request.TriggerKind!, tenant.DisplayName, baseUrl);
                body = Services.Email.AutomationMergeFields.Render(body, values, htmlEncode: true);
                preview = string.IsNullOrWhiteSpace(preview) ? null : Services.Email.AutomationMergeFields.Render(preview, values, htmlEncode: false);
            }
            var footer = "<hr style=\"border:none;border-top:1px solid #e5e7eb;margin:16px 0 8px\">"
                + $"<p style=\"font-size:12px;color:#9ca3af\">You're receiving this because you subscribed to updates from {System.Net.WebUtility.HtmlEncode(tenant.DisplayName)}. <a href=\"#\" style=\"color:#9ca3af\">Unsubscribe</a>.</p>";
            return new ApiResponses().OkResult(new CampaignPreviewResponse
            {
                Html = Services.Email.EmailHtml.Compose(body, preview, brand, footer),
            });
        }

        /// <summary>Events, event types, and pass products this tenant can address a campaign to.</summary>
        [HttpGet("Audience/Options")]
        public async Task<IActionResult> AudienceOptions()
        {
            var o = await _audiences.ListOptions(_tenantContext.TenantId);
            return new ApiResponses().OkResult(new CampaignAudienceOptionsResponse
            {
                Events = o.Events.Select(e => new CampaignAudienceEventOptionDto
                {
                    Id = e.Id, Title = e.Title, Status = e.Status, EventTypeName = e.EventTypeName,
                    StartsAtUtc = DateTime.SpecifyKind(e.StartsAt, DateTimeKind.Utc),
                }).ToList(),
                EventTypes = o.EventTypes.Select(t => new CampaignAudienceNamedOptionDto { Id = t.Id, Name = t.Name, IsActive = t.IsActive }).ToList(),
                PassProducts = o.PassProducts.Select(p => new CampaignAudienceNamedOptionDto { Id = p.Id, Name = p.Name, IsActive = p.IsActive }).ToList(),
            });
        }

        /// <summary>
        /// Live size of an audience while the editor is open: distinct addresses, and how many of
        /// them the suppression list will skip. Same resolution the send uses.
        /// </summary>
        [HttpGet("Audience/Count")]
        public async Task<IActionResult> AudienceCount([FromQuery] string? kind, [FromQuery] Guid? eventId,
            [FromQuery] Guid? eventTypeId, [FromQuery] Guid? passProductId,
            [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc)
        {
            var resolved = await ResolveAudience(kind, new CampaignAudienceConfigDto
            {
                EventId = eventId, EventTypeId = eventTypeId, PassProductId = passProductId, FromUtc = fromUtc, ToUtc = toUtc,
            });
            if (resolved.Error is not null) return new ApiResponses().BadRequestResult(resolved.Error);

            var recipients = await _audiences.ListRecipients(_tenantContext.TenantId, resolved.Kind, resolved.Config);
            var blocklist = await _suppression.ListMarketingBlocklist(_tenantContext.TenantId);
            var suppressed = recipients.Count(r => blocklist.Contains(r.Email));
            return new ApiResponses().OkResult(new CampaignAudienceCountResponse
            {
                Kind = resolved.Kind, Label = resolved.Label, Recipients = recipients.Count, Suppressed = suppressed,
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertCampaignRequest request)
        {
            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }
            var audience = await ResolveAudience(request.AudienceKind, request.AudienceConfig);
            if (audience.Error is not null) return new ApiResponses().BadRequestResult(audience.Error);

            var c = new EmailCampaign
            {
                TenantId = _tenantContext.TenantId,
                Subject = request.Subject.Trim(),
                BodyHtml = request.BodyHtml,
                BodyText = request.BodyText,
                PreviewText = Trim(request.PreviewText),
                Status = "draft",
                CreatedByUserId = userId,
                AudienceKind = audience.Kind,
                AudienceConfig = audience.Config.ToJson(),
            };
            c.Id = await _campaigns.Create(c);
            return new ApiResponses().OkResult(ToDetail(c, audience.Label));
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
            var audience = await ResolveAudience(request.AudienceKind, request.AudienceConfig);
            if (audience.Error is not null) return new ApiResponses().BadRequestResult(audience.Error);

            existing.Subject = request.Subject.Trim();
            existing.BodyHtml = request.BodyHtml;
            existing.BodyText = request.BodyText;
            existing.PreviewText = Trim(request.PreviewText);
            existing.AudienceKind = audience.Kind;
            existing.AudienceConfig = audience.Config.ToJson();
            await _campaigns.Update(existing);
            return new ApiResponses().OkResult(ToDetail(existing, audience.Label));
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

            // Resolve the audience NOW (send time), not when the draft was written, so a camp
            // that sold more tickets since the draft reaches everyone who bought.
            var audienceConfig = CampaignAudienceConfig.Parse(campaign.AudienceConfig);
            var audienceLabel = await _audiences.DescribeAudience(_tenantContext.TenantId, campaign.AudienceKind, audienceConfig);
            if (audienceLabel is null)
            {
                return new ApiResponses().BadRequestResult(
                    "This campaign's audience no longer exists (the event, event type, or pass was removed). Edit the campaign and pick another audience.");
            }
            var recipients = await _audiences.ListRecipients(_tenantContext.TenantId, campaign.AudienceKind, audienceConfig);
            if (recipients.Count == 0)
            {
                return new ApiResponses().BadRequestResult($"Nobody is in this audience yet ({audienceLabel}); nothing to send.");
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
                return new ApiResponses().BadRequestResult("Everyone in this audience is on the suppression list; nothing to send.");
            }

            // A future time (60s grace for clock skew) schedules; otherwise send now. The
            // audience is snapshotted now; the handler re-checks suppression at delivery time
            // so opt-outs between scheduling and sending are still honored.
            var runAt = scheduledForUtc?.ToUniversalTime();
            var isScheduled = runAt.HasValue && runAt.Value > DateTime.UtcNow.AddSeconds(60);

            await _campaigns.CreateSendRows(id, recipients.Select(r => new EmailCampaignSend
            {
                SubscriberId = r.SubscriberId,
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
                    : $"Sending to {recipients.Count} recipient{(recipients.Count == 1 ? "" : "s")} ({audienceLabel}) in the background{suppressedNote}.",
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

        /// <summary>
        /// Validates and normalizes an audience from a request. Subscribers is the default when
        /// nothing is sent (older clients, and the common case). A target id that does not belong
        /// to this tenant fails here with a plain message rather than silently addressing nobody.
        /// </summary>
        private async Task<(string Kind, CampaignAudienceConfig Config, string Label, string? Error)> ResolveAudience(
            string? kindRaw, CampaignAudienceConfigDto? dto)
        {
            var kind = string.IsNullOrWhiteSpace(kindRaw) ? CampaignAudienceKinds.Subscribers : kindRaw.Trim().ToLowerInvariant();
            if (!CampaignAudienceKinds.IsValid(kind))
            {
                return (kind, new CampaignAudienceConfig(), string.Empty, "Unknown audience. Choose subscribers, an event, an event type, or a pass product.");
            }
            var config = new CampaignAudienceConfig();
            switch (kind)
            {
                case CampaignAudienceKinds.Event:
                    if (dto?.EventId is null) return (kind, config, string.Empty, "Pick the event whose purchasers this campaign goes to.");
                    config.EventId = dto.EventId;
                    break;
                case CampaignAudienceKinds.EventType:
                    if (dto?.EventTypeId is null) return (kind, config, string.Empty, "Pick the event type whose purchasers this campaign goes to.");
                    config.EventTypeId = dto.EventTypeId;
                    config.FromUtc = dto.FromUtc?.ToUniversalTime();
                    config.ToUtc = dto.ToUtc?.ToUniversalTime();
                    if (config.FromUtc is not null && config.ToUtc is not null && config.ToUtc <= config.FromUtc)
                        return (kind, config, string.Empty, "The event date range is backwards: the end must be after the start.");
                    break;
                case CampaignAudienceKinds.PassProduct:
                    if (dto?.PassProductId is null) return (kind, config, string.Empty, "Pick the pass product whose holders this campaign goes to.");
                    config.PassProductId = dto.PassProductId;
                    break;
            }
            var label = await _audiences.DescribeAudience(_tenantContext.TenantId, kind, config);
            if (label is null)
            {
                return (kind, config, string.Empty, "That event, event type, or pass product was not found for this track.");
            }
            return (kind, config, label, null);
        }

        private async Task<string> LabelFor(EmailCampaign c)
            => await _audiences.DescribeAudience(_tenantContext.TenantId, c.AudienceKind, CampaignAudienceConfig.Parse(c.AudienceConfig))
               ?? "Audience no longer exists";

        private static CampaignAudienceConfigDto ToConfigDto(EmailCampaign c)
        {
            var cfg = CampaignAudienceConfig.Parse(c.AudienceConfig);
            return new CampaignAudienceConfigDto
            {
                EventId = cfg.EventId, EventTypeId = cfg.EventTypeId, PassProductId = cfg.PassProductId,
                FromUtc = cfg.FromUtc.HasValue ? DateTime.SpecifyKind(cfg.FromUtc.Value, DateTimeKind.Utc) : null,
                ToUtc = cfg.ToUtc.HasValue ? DateTime.SpecifyKind(cfg.ToUtc.Value, DateTimeKind.Utc) : null,
            };
        }

        private static CampaignListItem ToListItem(EmailCampaign c, string audienceLabel) => new()
        {
            Id = c.Id,
            Subject = c.Subject,
            Status = c.Status,
            RecipientCount = c.RecipientCount,
            AudienceKind = c.AudienceKind,
            AudienceLabel = audienceLabel,
            AudienceConfig = ToConfigDto(c),
            SentAtUtc = c.SentAt.HasValue ? DateTime.SpecifyKind(c.SentAt.Value, DateTimeKind.Utc) : null,
            ScheduledForUtc = c.ScheduledFor.HasValue ? DateTime.SpecifyKind(c.ScheduledFor.Value, DateTimeKind.Utc) : null,
            CreatedAtUtc = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
        };

        private static CampaignDetail ToDetail(EmailCampaign c, string audienceLabel) => new()
        {
            Id = c.Id,
            Subject = c.Subject,
            Status = c.Status,
            RecipientCount = c.RecipientCount,
            AudienceKind = c.AudienceKind,
            AudienceLabel = audienceLabel,
            AudienceConfig = ToConfigDto(c),
            SentAtUtc = c.SentAt.HasValue ? DateTime.SpecifyKind(c.SentAt.Value, DateTimeKind.Utc) : null,
            CreatedAtUtc = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
            BodyHtml = c.BodyHtml,
            BodyText = c.BodyText,
            PreviewText = c.PreviewText,
        };

        private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }
    }
}
