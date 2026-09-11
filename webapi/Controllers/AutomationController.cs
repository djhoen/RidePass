using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Email;
using Services.Helpers;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Newsletter;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Drip campaigns. Separate from <see cref="CampaignController"/> because a broadcast and an
    /// automation have different lifecycles: one is sent and done, one runs forever.
    /// Design: docs/drip-campaigns.md and docs/dynamic-campaigns-plan.md.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
    public class AutomationController : ControllerBase
    {
        private readonly IMarketingAutomationRepository _automations;
        private readonly ISeasonPassRepository _passes;
        private readonly ICampaignAudienceRepository _audiences;
        private readonly IAudienceRepository _savedAudiences;
        private readonly ITenantBrandingRepository _brandings;
        private readonly IEmailEngagementRepository _engagement;
        private readonly ISmtpEmailer _emailer;
        private readonly ITenantContext _tenantContext;
        private readonly IConfiguration _config;
        private readonly ILogger<AutomationController> _logger;

        public AutomationController(
            IMarketingAutomationRepository automations,
            ISeasonPassRepository passes,
            ICampaignAudienceRepository audiences,
            IAudienceRepository savedAudiences,
            ITenantBrandingRepository brandings,
            IEmailEngagementRepository engagement,
            ISmtpEmailer emailer,
            ITenantContext tenantContext,
            IConfiguration config,
            ILogger<AutomationController> logger)
        {
            _automations = automations;
            _passes = passes;
            _audiences = audiences;
            _savedAudiences = savedAudiences;
            _brandings = brandings;
            _engagement = engagement;
            _emailer = emailer;
            _tenantContext = tenantContext;
            _config = config;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _automations.ListForTenant(_tenantContext.TenantId);
            var stats = await _automations.GetStats(_tenantContext.TenantId);
            var engagement = await _engagement.GetAutomationStats(_tenantContext.TenantId);

            var items = new List<AutomationListItem>();
            foreach (var a in rows)
            {
                var steps = await _automations.ListSteps(a.Id, _tenantContext.TenantId);
                items.Add(await ToListItem(a, steps, stats, engagement.GetValueOrDefault(a.Id)));
            }
            return new ApiResponses().OkResult(items);
        }

        /// <summary>
        /// Everything the editor needs: the triggers with their anchors and merge fields, plus the
        /// events, event types, and pass products this tenant can target. Served from here so a
        /// marketing user can build an automation without catalog rights.
        /// </summary>
        [HttpGet("TriggerOptions")]
        public async Task<IActionResult> TriggerOptions()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var o = await _audiences.ListOptions(_tenantContext.TenantId);
            return new ApiResponses().OkResult(new AutomationTriggerOptionsResponse
            {
                Triggers = AutomationTriggers.Kinds.Select(k => new AutomationTriggerOption
                {
                    Kind = k,
                    Label = AutomationTriggers.Label(k),
                    Anchors = AutomationTriggers.AnchorsFor(k)
                        .Select(an => new AutomationAnchorOption { Value = an, Phrase = AutomationTriggers.AnchorPhrase(an, k) })
                        .ToList(),
                    MergeFields = AutomationMergeFields.AvailableFor(k)
                        .Select(x => new MergeFieldItem { Token = x.Token, Description = x.Description })
                        .ToList(),
                }).ToList(),
                Events = o.Events.Select(e => new CampaignAudienceEventOptionDto
                {
                    Id = e.Id, Title = e.Title, Status = e.Status, EventTypeName = e.EventTypeName,
                    StartsAtUtc = DateTime.SpecifyKind(e.StartsAt, DateTimeKind.Utc),
                }).ToList(),
                EventTypes = o.EventTypes.Select(t => new CampaignAudienceNamedOptionDto { Id = t.Id, Name = t.Name, IsActive = t.IsActive }).ToList(),
                PassProducts = o.PassProducts.Select(p => new CampaignAudienceNamedOptionDto { Id = p.Id, Name = p.Name, IsActive = p.IsActive }).ToList(),
                Audiences = (await _savedAudiences.ListForTenant(_tenantContext.TenantId))
                    .Select(x => new CampaignAudienceNamedOptionDto { Id = x.Id, Name = x.Name, IsActive = true }).ToList(),
            });
        }

        /// <summary>Pass products for older clients of the trigger select.</summary>
        [HttpGet("Products")]
        public async Task<IActionResult> Products()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var products = await _passes.ListProductsForTenant(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(products
                .Select(p => new { id = p.Id, name = p.Name, isActive = p.IsActive })
                .ToList());
        }

        [HttpGet("MergeFields")]
        public IActionResult MergeFields([FromQuery] string? triggerKind = null)
        {
            var fields = AutomationTriggers.IsKind(triggerKind)
                ? AutomationMergeFields.AvailableFor(triggerKind!)
                : AutomationMergeFields.Available;
            return new ApiResponses().OkResult(fields
                .Select(x => new MergeFieldItem { Token = x.Token, Description = x.Description })
                .ToList());
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var a = await _automations.GetById(id, _tenantContext.TenantId);
            if (a is null) return new ApiResponses().NotFoundResult("Automation not found.");

            var steps = await _automations.ListSteps(a.Id, _tenantContext.TenantId);
            var stats = await _automations.GetStats(_tenantContext.TenantId);
            var stepStats = await _automations.GetStepStats(a.Id, _tenantContext.TenantId);
            var engagement = await _engagement.GetAutomationStepStats(a.Id, _tenantContext.TenantId);
            var basic = await ToListItem(a, steps, stats);

            return new ApiResponses().OkResult(new AutomationDetail
            {
                Id = basic.Id,
                Name = basic.Name,
                TriggerKind = basic.TriggerKind,
                TriggerLabel = basic.TriggerLabel,
                FromProductId = basic.FromProductId,
                FromProductName = basic.FromProductName,
                EventId = basic.EventId,
                EventTypeId = basic.EventTypeId,
                AudienceId = basic.AudienceId,
                IsActive = basic.IsActive,
                StepCount = basic.StepCount,
                FirstDelayDays = basic.FirstDelayDays,
                FirstStepLabel = basic.FirstStepLabel,
                Sent = basic.Sent,
                Failed = basic.Failed,
                Skipped = basic.Skipped,
                Conversions = basic.Conversions,
                EnrolFromUtc = basic.EnrolFromUtc,
                UpdatedAt = basic.UpdatedAt,
                StopOnUpgrade = a.StopOnUpgrade,
                StopWhenUsedUp = a.StopWhenUsedUp,
                SendWindowStart = FormatTime(a.SendWindowStart),
                SendWindowEnd = FormatTime(a.SendWindowEnd),
                Steps = steps.Select(s => ToStepItem(s, a.TriggerKind, stepStats.GetValueOrDefault(s.Id), engagement.GetValueOrDefault(s.Id))).ToList(),
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertAutomationRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var parsed = await ValidateAndParse(request);
            if (parsed.Error is not null) return new ApiResponses().BadRequestResult(parsed.Error);

            var id = await _automations.Create(new MarketingAutomation
            {
                TenantId = _tenantContext.TenantId,
                Name = request.Name.Trim(),
                TriggerKind = parsed.Kind,
                TriggerConfig = parsed.Config.ToJson(),
                StopOnUpgrade = request.StopOnUpgrade,
                StopWhenUsedUp = request.StopWhenUsedUp,
                SendWindowStart = ParseTime(request.SendWindowStart),
                SendWindowEnd = ParseTime(request.SendWindowEnd),
                CreatedByUserId = CurrentUserId(),
            });
            await _automations.ReplaceSteps(id, _tenantContext.TenantId, parsed.Steps);
            return new ApiResponses().OkResult(new { id });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertAutomationRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = await _automations.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Automation not found.");
            var parsed = await ValidateAndParse(request);
            if (parsed.Error is not null) return new ApiResponses().BadRequestResult(parsed.Error);

            // Editing a running automation is fine: steps keep their ids, so the send history
            // survives and riders who already received an email are not sent it again. A step the
            // admin deletes takes its history with it, which is the meaning of deleting it.
            existing.Name = request.Name.Trim();
            existing.TriggerKind = parsed.Kind;
            existing.TriggerConfig = parsed.Config.ToJson();
            existing.StopOnUpgrade = request.StopOnUpgrade;
            existing.StopWhenUsedUp = request.StopWhenUsedUp;
            existing.SendWindowStart = ParseTime(request.SendWindowStart);
            existing.SendWindowEnd = ParseTime(request.SendWindowEnd);
            await _automations.Update(existing);
            await _automations.ReplaceSteps(id, _tenantContext.TenantId, parsed.Steps);
            return new ApiResponses().OkResult();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _automations.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        /// <summary>
        /// What arming would cost, for the confirm dialog. Automations bill per email and keep
        /// billing, so the tenant sees the bill before the switch, not on their next payout.
        /// </summary>
        [HttpGet("{id:guid}/Estimate")]
        public async Task<IActionResult> Estimate(Guid id, [FromQuery] bool newPurchasesOnly = true)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var a = await _automations.GetById(id, _tenantContext.TenantId);
            if (a is null) return new ApiResponses().NotFoundResult("Automation not found.");
            var steps = await _automations.ListSteps(a.Id, _tenantContext.TenantId);
            if (steps.Count == 0) return new ApiResponses().BadRequestResult("This automation has no emails yet.");

            // Estimated against the FIRST step: it is the one whose backlog lands immediately.
            await RefreshAudienceFor(a);
            var now = DateTime.UtcNow;
            var (backlog, last30) = await _automations.EstimateAudience(
                a, steps[0], now, TenantToday(now),
                // Mirrors what SetActive would stamp, so the estimate matches the outcome.
                newPurchasesOnly ? now : a.EnrolFromUtc);

            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthToDate = await _automations.CountSentEmailsInMonth(_tenantContext.TenantId, monthStart);

            return new ApiResponses().OkResult(new AutomationEstimate
            {
                BacklogCount = backlog,
                BacklogChargeCents = EmailPricing.MarginalChargeCents(monthToDate, backlog),
                Last30DayRate = last30,
                // Priced from a clean month so the forecast is a monthly rate, not "the rest of
                // this month at whatever tier we happen to be in".
                OngoingChargeCents = EmailPricing.MarginalChargeCents(0, last30),
            });
        }

        [HttpPost("{id:guid}/Activate")]
        public async Task<IActionResult> Activate(Guid id, [FromBody] ActivateAutomationRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var a = await _automations.GetById(id, _tenantContext.TenantId);
            if (a is null) return new ApiResponses().NotFoundResult("Automation not found.");

            if (request.IsActive)
            {
                var steps = await _automations.ListSteps(a.Id, _tenantContext.TenantId);
                if (steps.Count == 0)
                {
                    return new ApiResponses().BadRequestResult(
                        "Add at least one email before turning this automation on.");
                }
                if (!_emailer.IsConfigured)
                {
                    return new ApiResponses().BadRequestResult(
                        "Email isn't set up for this site yet, so an automation would never send. " +
                        "Contact support to finish email setup first.");
                }
            }

            // Membership is brought up to date before the enrol-from stamp, so "only riders from
            // now on" means exactly that and the backlog choice covers everyone already in.
            if (request.IsActive) await RefreshAudienceFor(a);
            await _automations.SetActive(id, _tenantContext.TenantId, request.IsActive,
                request.IsActive && request.NewPurchasesOnly ? DateTime.UtcNow : null);
            return new ApiResponses().OkResult();
        }

        /// <summary>The audience trigger reads audience_member; make sure it reflects right now.</summary>
        private async Task RefreshAudienceFor(MarketingAutomation a)
        {
            if (a.TriggerKind != AutomationTriggers.AudienceJoined) return;
            var cfg = AutomationTriggerConfig.For(a);
            if (cfg.AudienceId is not Guid aid) return;
            var audience = await _savedAudiences.GetById(aid, _tenantContext.TenantId);
            if (audience is not null) await _savedAudiences.RefreshMembers(audience);
        }

        /// <summary>
        /// Render one step with a real purchase's merge values and send it to the caller. Nothing
        /// about a drip is verifiable by reading the editor, and the first live send is a bad time
        /// to find out a merge field is wrong or an email would land after the camp.
        /// </summary>
        [HttpPost("{id:guid}/TestSend")]
        public async Task<IActionResult> TestSend(Guid id, [FromBody] TestSendRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_emailer.IsConfigured)
            {
                return new ApiResponses().BadRequestResult("Email isn't set up for this site yet, so a test can't be sent.");
            }
            var a = await _automations.GetById(id, _tenantContext.TenantId);
            if (a is null) return new ApiResponses().NotFoundResult("Automation not found.");
            var steps = await _automations.ListSteps(a.Id, _tenantContext.TenantId);
            if (request.StepIndex >= steps.Count)
            {
                return new ApiResponses().BadRequestResult("That email doesn't exist on this automation.");
            }
            var step = steps[request.StepIndex];

            var baseUrl = TenantBaseUrl();
            var trackName = _tenantContext.Tenant?.DisplayName ?? "the track";
            var timezone = _tenantContext.Tenant?.Timezone;
            var sample = await _automations.SampleSubject(a);
            var values = sample is null
                ? AutomationMergeFields.Sample(a.TriggerKind, trackName, baseUrl)
                : AutomationMergeFields.For(sample, trackName, baseUrl, timezone);

            // Show the admin WHEN this would go, not just what it says: a wrong anchor sends
            // "what to bring" after the camp, and only the date makes that visible.
            var (wouldSendOn, wouldSkip) = sample is null ? (null, false) : WhenWouldSend(step, sample, timezone);
            var timingNote = sample is null
                ? ""
                : wouldSkip
                    ? "For this rider the email would be SKIPPED: they bought after its send time."
                    : $"For this rider it would send on {wouldSendOn}.";

            var subject = "[TEST] " + AutomationMergeFields.Render(step.Subject, values, htmlEncode: false);
            var brand = EmailBranding.From(_tenantContext.Tenant!, await _brandings.GetByTenantId(_tenantContext.TenantId), baseUrl);
            var previewText = string.IsNullOrWhiteSpace(step.PreviewText) ? null
                : AutomationMergeFields.Render(step.PreviewText, values, htmlEncode: false);
            var html = EmailHtml.Compose(
                AutomationMergeFields.Render(step.BodyHtml, values, htmlEncode: true), previewText, brand,
                $@"<hr style=""border:none;border-top:1px solid #e5e7eb;margin:16px 0 8px"">
<p style=""font-size:12px;color:#9ca3af"">Test send from {System.Net.WebUtility.HtmlEncode(trackName)}.
Merge fields were filled in from {(sample is null ? "sample data (nothing sold yet)" : "a real purchase")}. {System.Net.WebUtility.HtmlEncode(timingNote)}</p>");

            var ok = await _emailer.Send(request.ToEmail, subject, html, null,
                TenantEmailIdentity.For(_tenantContext.Tenant));
            if (!ok)
            {
                _logger.LogWarning("Automation {Id} test send to {Email} failed.", id, request.ToEmail);
                return new ApiResponses().BadRequestResult(
                    "The test email could not be sent. The email service rejected it; check the address and try again.");
            }
            return new ApiResponses().OkResult(new TestSendResponse
            {
                UsedRealSubject = sample is not null,
                SampleName = sample?.ProductName,
                WouldSendOn = wouldSendOn,
                WouldSkip = wouldSkip,
            });
        }

        /// <summary>
        /// Backing data for the upgrades page panel: which automations market a pass product, and
        /// how they are doing. Read-only, so ReportsView would be too narrow and CatalogManage too
        /// wide; the upgrades page calls it and tolerates a 403 by hiding the panel.
        /// </summary>
        [HttpGet("ForUpgrades")]
        public async Task<IActionResult> ForUpgrades()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _automations.ListByTriggerProduct(_tenantContext.TenantId);
            var stats = await _automations.GetStats(_tenantContext.TenantId);

            var items = new List<UpgradeAutomationStatus>();
            foreach (var a in rows)
            {
                var steps = await _automations.ListSteps(a.Id, _tenantContext.TenantId);
                stats.TryGetValue(a.Id, out var st);
                items.Add(new UpgradeAutomationStatus
                {
                    FromProductId = AutomationTriggerConfig.For(a).FromProductId,
                    AutomationId = a.Id,
                    Name = a.Name,
                    IsActive = a.IsActive,
                    FirstDelayDays = FirstDelayDays(steps),
                    Sent = st?.Sent ?? 0,
                    Conversions = st?.Conversions ?? 0,
                });
            }
            return new ApiResponses().OkResult(items);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Everything that can be wrong with a request, in the order a tenant would fix it. Steps
        /// come back normalized (anchor defaulted, offsets signed, fixed dates parsed).
        /// </summary>
        private async Task<(string Kind, AutomationTriggerConfig Config, List<MarketingAutomationStep> Steps, string? Error)> ValidateAndParse(
            UpsertAutomationRequest request)
        {
            var kind = string.IsNullOrWhiteSpace(request.TriggerKind) ? AutomationTriggers.SeasonPassPurchased : request.TriggerKind.Trim();
            var config = new AutomationTriggerConfig();
            var steps = new List<MarketingAutomationStep>();

            if (!AutomationTriggers.IsKind(kind))
            {
                return (kind, config, steps, "Pick what starts this automation: a pass sale, an event ticket sale, a newsletter signup, or someone joining an audience.");
            }
            if (request.Steps.Count == 0)
            {
                return (kind, config, steps, "An automation needs at least one email.");
            }

            // Trigger target, checked against THIS tenant so a foreign id fails plainly.
            if (kind == AutomationTriggers.SeasonPassPurchased)
            {
                if (request.FromProductId is Guid pid)
                {
                    var product = await _passes.GetProduct(pid, _tenantContext.TenantId);
                    if (product is null) return (kind, config, steps, "That pass product wasn't found.");
                    config.FromProductId = pid;
                }
            }
            else if (kind == AutomationTriggers.AudienceJoined)
            {
                if (request.AudienceId is not Guid audId)
                {
                    return (kind, config, steps, "Pick the audience that starts this automation.");
                }
                var audience = await _savedAudiences.GetById(audId, _tenantContext.TenantId);
                if (audience is null) return (kind, config, steps, "That audience wasn't found.");
                config.AudienceId = audId;
            }
            else if (kind == AutomationTriggers.EventTicketPurchased)
            {
                if ((request.EventId is null) == (request.EventTypeId is null))
                {
                    return (kind, config, steps, "Pick either one event or one event type for this automation.");
                }
                if (request.EventId is Guid eid)
                {
                    var name = await _audiences.TargetName(_tenantContext.TenantId, CampaignAudienceKinds.Event,
                        new CampaignAudienceConfig { EventId = eid });
                    if (name is null) return (kind, config, steps, "That event wasn't found.");
                    config.EventId = eid;
                }
                else
                {
                    var name = await _audiences.TargetName(_tenantContext.TenantId, CampaignAudienceKinds.EventType,
                        new CampaignAudienceConfig { EventTypeId = request.EventTypeId });
                    if (name is null) return (kind, config, steps, "That event type wasn't found.");
                    config.EventTypeId = request.EventTypeId;
                }
            }

            // Half a window is ambiguous: the sweep would have to guess which side of the day it
            // meant, so reject it rather than pick.
            if (string.IsNullOrWhiteSpace(request.SendWindowStart) != string.IsNullOrWhiteSpace(request.SendWindowEnd))
            {
                return (kind, config, steps, "A send window needs both a start and an end time, or neither.");
            }
            if (ParseTime(request.SendWindowStart) is null != string.IsNullOrWhiteSpace(request.SendWindowStart))
            {
                return (kind, config, steps, "The send window times must look like 09:00.");
            }

            var seen = new HashSet<string>();
            var n = 0;
            foreach (var s in request.Steps)
            {
                n++;
                var anchor = string.IsNullOrWhiteSpace(s.Anchor) ? AutomationTriggers.Anchors.Purchase : s.Anchor.Trim();
                var offset = s.OffsetDays ?? s.DelayDays;
                DateTime? sendOn = null;
                if (anchor == AutomationTriggers.Anchors.FixedDate)
                {
                    if (!DateTime.TryParseExact(s.SendOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        return (kind, config, steps, $"Email {n}: pick the date it should send on.");
                    }
                    sendOn = d.Date;
                    offset = 0;
                }
                var problem = AutomationTriggers.ValidateStep(kind, anchor, offset, sendOn);
                if (problem is not null) return (kind, config, steps, $"Email {n}: {problem}");

                // Two emails at the same moment read as a bug to the recipient.
                var key = $"{anchor}|{offset}|{sendOn:yyyy-MM-dd}";
                if (!seen.Add(key))
                {
                    return (kind, config, steps, $"Email {n} is set to send at the same time as an earlier one. Give each email its own timing.");
                }

                steps.Add(new MarketingAutomationStep
                {
                    Id = s.Id ?? Guid.Empty,
                    Anchor = anchor,
                    OffsetDays = offset,
                    DelayDays = anchor == AutomationTriggers.Anchors.Purchase ? Math.Max(0, offset) : 0,
                    SendOn = sendOn,
                    Subject = s.Subject.Trim(),
                    BodyHtml = s.BodyHtml,
                    BodyText = s.BodyText,
                    PreviewText = string.IsNullOrWhiteSpace(s.PreviewText) ? null : s.PreviewText.Trim(),
                });
            }

            return (kind, config, steps, null);
        }

        private async Task<AutomationListItem> ToListItem(
            MarketingAutomation a, List<MarketingAutomationStep> steps, Dictionary<Guid, MarketingAutomationStats> stats,
            EmailEngagementStats? eng = null)
        {
            stats.TryGetValue(a.Id, out var st);
            var cfg = AutomationTriggerConfig.For(a);
            string? targetName = null;
            if (a.TriggerKind == AutomationTriggers.EventTicketPurchased)
            {
                targetName = cfg.EventId is not null
                    ? await _audiences.TargetName(_tenantContext.TenantId, CampaignAudienceKinds.Event, new CampaignAudienceConfig { EventId = cfg.EventId })
                    : cfg.EventTypeId is not null
                        ? "any " + await _audiences.TargetName(_tenantContext.TenantId, CampaignAudienceKinds.EventType, new CampaignAudienceConfig { EventTypeId = cfg.EventTypeId })
                        : null;
            }
            else if (a.TriggerKind == AutomationTriggers.AudienceJoined)
            {
                targetName = cfg.AudienceId is Guid aid
                    ? (await _savedAudiences.GetById(aid, _tenantContext.TenantId))?.Name ?? "a removed audience"
                    : null;
            }
            else if (cfg.FromProductId is not null)
            {
                targetName = await _audiences.TargetName(_tenantContext.TenantId, CampaignAudienceKinds.PassProduct, new CampaignAudienceConfig { PassProductId = cfg.FromProductId });
            }

            var first = steps.FirstOrDefault();
            return new AutomationListItem
            {
                Id = a.Id,
                Name = a.Name,
                TriggerKind = a.TriggerKind,
                TriggerLabel = AutomationTriggers.DescribeTrigger(a.TriggerKind, targetName),
                FromProductId = cfg.FromProductId,
                FromProductName = a.TriggerKind == AutomationTriggers.SeasonPassPurchased ? targetName : null,
                EventId = cfg.EventId,
                EventTypeId = cfg.EventTypeId,
                AudienceId = cfg.AudienceId,
                IsActive = a.IsActive,
                StepCount = steps.Count,
                FirstDelayDays = FirstDelayDays(steps),
                FirstStepLabel = first is null ? null : AutomationTriggers.DescribeStep(first.Anchor, first.OffsetDays, first.SendOn, a.TriggerKind),
                Sent = st?.Sent ?? 0,
                Failed = st?.Failed ?? 0,
                Skipped = st?.Skipped ?? 0,
                Conversions = st?.Conversions ?? 0,
                UniqueOpens = eng?.UniqueOpens ?? 0,
                UniqueClicks = eng?.UniqueClicks ?? 0,
                EnrolFromUtc = a.EnrolFromUtc,
                UpdatedAt = a.UpdatedAt,
            };
        }

        private static AutomationStepItem ToStepItem(MarketingAutomationStep s, string triggerKind,
            MarketingAutomationStepStats? st = null, EmailEngagementStats? eng = null) => new()
        {
            Id = s.Id,
            StepOrder = s.StepOrder,
            DelayDays = s.DelayDays,
            Anchor = s.Anchor,
            OffsetDays = s.OffsetDays,
            SendOn = s.SendOn?.ToString("yyyy-MM-dd"),
            Label = AutomationTriggers.DescribeStep(s.Anchor, s.OffsetDays, s.SendOn, triggerKind),
            Subject = s.Subject,
            BodyHtml = s.BodyHtml,
            BodyText = s.BodyText,
            PreviewText = s.PreviewText,
            Sent = st?.Sent ?? 0,
            Failed = st?.Failed ?? 0,
            Skipped = st?.Skipped ?? 0,
            LastSentAtUtc = st?.LastSentAt is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : null,
            UniqueOpens = eng?.UniqueOpens ?? 0,
            UniqueClicks = eng?.UniqueClicks ?? 0,
            SkipReasons = st?.SkipReasons.Select(r => new AutomationSkipReasonItem
            {
                Status = r.Status, Reason = string.IsNullOrEmpty(r.Reason) ? "No reason recorded" : r.Reason, Count = r.Count,
            }).ToList() ?? new List<AutomationSkipReasonItem>(),
        };

        private static int? FirstDelayDays(List<MarketingAutomationStep> steps)
        {
            var first = steps.FirstOrDefault();
            return first is null ? null : first.Anchor == AutomationTriggers.Anchors.Purchase ? first.OffsetDays : null;
        }

        /// <summary>Tenant-local calendar date the step would send for a subject, or a skip.</summary>
        private static (string? WouldSendOn, bool WouldSkip) WhenWouldSend(MarketingAutomationStep step, AutomationSubject s, string? timezone)
        {
            DateTime? anchorUtc = step.Anchor switch
            {
                AutomationTriggers.Anchors.Purchase => s.PurchasedAtUtc,
                AutomationTriggers.Anchors.EventStart => s.EventStartsAt,
                AutomationTriggers.Anchors.EventEnd => s.EventEndsAt,
                AutomationTriggers.Anchors.PassExpiry => s.ValidToDate,
                _ => null,
            };
            DateTime? whenUtc = step.Anchor == AutomationTriggers.Anchors.FixedDate
                ? step.SendOn
                : anchorUtc?.AddDays(step.OffsetDays);
            if (whenUtc is null) return (null, false);
            var skip = step.Anchor != AutomationTriggers.Anchors.Purchase && whenUtc.Value < s.PurchasedAtUtc;
            var local = step.Anchor == AutomationTriggers.Anchors.FixedDate ? whenUtc.Value : SendWindow.ToLocal(whenUtc.Value, timezone);
            return (local.ToString("MMMM d, yyyy"), skip);
        }

        private DateTime TenantToday(DateTime nowUtc) => SendWindow.ToLocal(nowUtc, _tenantContext.Tenant?.Timezone).Date;

        private static TimeSpan? ParseTime(string? hhmm) =>
            TimeSpan.TryParse(hhmm, out var t) ? t : null;

        private static string? FormatTime(TimeSpan? t) => t?.ToString(@"hh\:mm");

        private string TenantBaseUrl()
        {
            var rootDomain = _config["Tenant:RootDomain"] ?? _config["App:RootDomain"] ?? "ridepass.io";
            return $"https://{_tenantContext.Tenant?.Subdomain}.{rootDomain}";
        }

        private Guid? CurrentUserId() =>
            Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;
    }
}
