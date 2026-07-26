using System.Text.Json;
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
    /// Design: docs/drip-campaigns.md.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
    public class AutomationController : ControllerBase
    {
        private readonly IMarketingAutomationRepository _automations;
        private readonly ISeasonPassRepository _passes;
        private readonly ISmtpEmailer _emailer;
        private readonly ITenantContext _tenantContext;
        private readonly IConfiguration _config;
        private readonly ILogger<AutomationController> _logger;

        public AutomationController(
            IMarketingAutomationRepository automations,
            ISeasonPassRepository passes,
            ISmtpEmailer emailer,
            ITenantContext tenantContext,
            IConfiguration config,
            ILogger<AutomationController> logger)
        {
            _automations = automations;
            _passes = passes;
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
            var products = await _passes.ListProductsForTenant(_tenantContext.TenantId, activeOnly: false);

            var items = new List<AutomationListItem>();
            foreach (var a in rows)
            {
                var steps = await _automations.ListSteps(a.Id, _tenantContext.TenantId);
                items.Add(ToListItem(a, steps, stats, products));
            }
            return new ApiResponses().OkResult(items);
        }

        /// <summary>
        /// Pass products for the trigger select. Served from here rather than reusing the catalog
        /// endpoint so a marketing user can build an automation without also holding catalog
        /// rights. Employee products are excluded: they are staff grants, never an upgrade market.
        /// </summary>
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
        public IActionResult MergeFields() =>
            new ApiResponses().OkResult(AutomationMergeFields.Available
                .Select(x => new MergeFieldItem { Token = x.Token, Description = x.Description })
                .ToList());

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var a = await _automations.GetById(id, _tenantContext.TenantId);
            if (a is null) return new ApiResponses().NotFoundResult("Automation not found.");

            var steps = await _automations.ListSteps(a.Id, _tenantContext.TenantId);
            var stats = await _automations.GetStats(_tenantContext.TenantId);
            var products = await _passes.ListProductsForTenant(_tenantContext.TenantId, activeOnly: false);
            var basic = ToListItem(a, steps, stats, products);

            return new ApiResponses().OkResult(new AutomationDetail
            {
                Id = basic.Id,
                Name = basic.Name,
                TriggerKind = basic.TriggerKind,
                FromProductId = basic.FromProductId,
                FromProductName = basic.FromProductName,
                IsActive = basic.IsActive,
                StepCount = basic.StepCount,
                FirstDelayDays = basic.FirstDelayDays,
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
                Steps = steps.Select(s => new AutomationStepItem
                {
                    Id = s.Id,
                    StepOrder = s.StepOrder,
                    DelayDays = s.DelayDays,
                    Subject = s.Subject,
                    BodyHtml = s.BodyHtml,
                    BodyText = s.BodyText,
                }).ToList(),
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertAutomationRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var invalid = await Validate(request);
            if (invalid is not null) return invalid;

            var id = await _automations.Create(new MarketingAutomation
            {
                TenantId = _tenantContext.TenantId,
                Name = request.Name.Trim(),
                TriggerKind = "season_pass_purchased",
                TriggerConfig = TriggerConfigJson(request.FromProductId),
                StopOnUpgrade = request.StopOnUpgrade,
                StopWhenUsedUp = request.StopWhenUsedUp,
                SendWindowStart = ParseTime(request.SendWindowStart),
                SendWindowEnd = ParseTime(request.SendWindowEnd),
                CreatedByUserId = CurrentUserId(),
            });
            await _automations.ReplaceSteps(id, _tenantContext.TenantId, ToSteps(request));
            return new ApiResponses().OkResult(new { id });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertAutomationRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = await _automations.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Automation not found.");
            var invalid = await Validate(request);
            if (invalid is not null) return invalid;

            // Editing an ARMED automation's steps would delete their send rows (FK cascade) and
            // re-send everyone. Make them disarm first rather than silently re-mailing the list.
            if (existing.IsActive)
            {
                return new ApiResponses().BadRequestResult(
                    "Turn this automation off before editing it. Editing the emails while it's running " +
                    "would send them again to everyone who already got them.");
            }

            existing.Name = request.Name.Trim();
            existing.TriggerConfig = TriggerConfigJson(request.FromProductId);
            existing.StopOnUpgrade = request.StopOnUpgrade;
            existing.StopWhenUsedUp = request.StopWhenUsedUp;
            existing.SendWindowStart = ParseTime(request.SendWindowStart);
            existing.SendWindowEnd = ParseTime(request.SendWindowEnd);
            await _automations.Update(existing);
            await _automations.ReplaceSteps(id, _tenantContext.TenantId, ToSteps(request));
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
            var delayDays = steps[0].DelayDays;
            var (backlog, last30) = await _automations.EstimateAudience(
                _tenantContext.TenantId, FromProductId(a), delayDays,
                a.StopOnUpgrade, a.StopWhenUsedUp,
                // Mirrors what SetActive would stamp, so the estimate matches the outcome.
                newPurchasesOnly ? DateTime.UtcNow : a.EnrolFromUtc);

            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
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

            await _automations.SetActive(id, _tenantContext.TenantId, request.IsActive,
                request.IsActive && request.NewPurchasesOnly ? DateTime.UtcNow : null);
            return new ApiResponses().OkResult();
        }

        /// <summary>
        /// Render one step with a real pass's merge values and send it to the caller. Nothing about
        /// a drip is verifiable by reading the editor, and the first live send is a bad time to
        /// find out a merge field is wrong.
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
            var sample = await _automations.SampleSubject(_tenantContext.TenantId, FromProductId(a));
            var values = sample is null
                ? AutomationMergeFields.Sample(trackName, baseUrl)
                : AutomationMergeFields.For(sample, trackName, baseUrl);

            var subject = "[TEST] " + AutomationMergeFields.Render(step.Subject, values, htmlEncode: false);
            var html = AutomationMergeFields.Render(step.BodyHtml, values, htmlEncode: true)
                + $@"<hr style=""border:none;border-top:1px solid #e5e7eb;margin:24px 0 12px"">
<p style=""font-size:12px;color:#9ca3af"">Test send from {System.Net.WebUtility.HtmlEncode(trackName)}.
Merge fields were filled in from {(sample is null ? "sample data (no pass sold yet)" : "a real pass")}.</p>";

            var ok = await _emailer.Send(request.ToEmail, subject, html, null,
                TenantEmailIdentity.For(_tenantContext.Tenant));
            if (!ok)
            {
                _logger.LogWarning("Automation {Id} test send to {Email} failed.", id, request.ToEmail);
                return new ApiResponses().BadRequestResult(
                    "The test email could not be sent. The email service rejected it; check the address and try again.");
            }
            return new ApiResponses().OkResult(new
            {
                usedRealPass = sample is not null,
                // Named so the admin can tell whether "no upgrade price" is a template bug or
                // just a pass with no upgrade configured.
                sampleProduct = sample?.ProductName,
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
                    FromProductId = FromProductId(a),
                    AutomationId = a.Id,
                    Name = a.Name,
                    IsActive = a.IsActive,
                    FirstDelayDays = steps.Count > 0 ? steps[0].DelayDays : null,
                    Sent = st?.Sent ?? 0,
                    Conversions = st?.Conversions ?? 0,
                });
            }
            return new ApiResponses().OkResult(items);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private async Task<IActionResult?> Validate(UpsertAutomationRequest request)
        {
            if (request.Steps.Count == 0)
            {
                return new ApiResponses().BadRequestResult("An automation needs at least one email.");
            }
            // Half a window is ambiguous: the sweep would have to guess which side of the day it
            // meant, so reject it rather than pick.
            if (string.IsNullOrWhiteSpace(request.SendWindowStart) != string.IsNullOrWhiteSpace(request.SendWindowEnd))
            {
                return new ApiResponses().BadRequestResult(
                    "A send window needs both a start and an end time, or neither.");
            }
            if (ParseTime(request.SendWindowStart) is null != string.IsNullOrWhiteSpace(request.SendWindowStart))
            {
                return new ApiResponses().BadRequestResult("The send window times must look like 09:00.");
            }
            // Duplicate delays on the same automation mean two emails land the same day, which is
            // never what was meant and reads as a bug to the recipient.
            var delays = request.Steps.Select(s => s.DelayDays).ToList();
            if (delays.Distinct().Count() != delays.Count)
            {
                return new ApiResponses().BadRequestResult(
                    "Two emails are set to send the same number of days after purchase. Give each one its own delay.");
            }
            if (request.FromProductId is Guid pid)
            {
                var product = await _passes.GetProduct(pid, _tenantContext.TenantId);
                if (product is null) return new ApiResponses().BadRequestResult("That pass product wasn't found.");
            }
            return null;
        }

        private static IEnumerable<MarketingAutomationStep> ToSteps(UpsertAutomationRequest request) =>
            // Ordered by delay rather than by the order they were typed, so "step 2" always means
            // the one that sends second.
            request.Steps.OrderBy(s => s.DelayDays).Select(s => new MarketingAutomationStep
            {
                DelayDays = s.DelayDays,
                Subject = s.Subject.Trim(),
                BodyHtml = s.BodyHtml,
                BodyText = s.BodyText,
            });

        private AutomationListItem ToListItem(
            MarketingAutomation a,
            List<MarketingAutomationStep> steps,
            Dictionary<Guid, MarketingAutomationStats> stats,
            List<Services.Repositories.Data.PaymentData.SeasonPassProduct> products)
        {
            stats.TryGetValue(a.Id, out var st);
            var fromId = FromProductId(a);
            return new AutomationListItem
            {
                Id = a.Id,
                Name = a.Name,
                TriggerKind = a.TriggerKind,
                FromProductId = fromId,
                FromProductName = fromId is Guid f ? products.FirstOrDefault(p => p.Id == f)?.Name : null,
                IsActive = a.IsActive,
                StepCount = steps.Count,
                FirstDelayDays = steps.Count > 0 ? steps[0].DelayDays : null,
                Sent = st?.Sent ?? 0,
                Failed = st?.Failed ?? 0,
                Skipped = st?.Skipped ?? 0,
                Conversions = st?.Conversions ?? 0,
                EnrolFromUtc = a.EnrolFromUtc,
                UpdatedAt = a.UpdatedAt,
            };
        }

        private static string TriggerConfigJson(Guid? fromProductId) =>
            JsonSerializer.Serialize(new { fromProductId });

        /// <summary>Reads the trigger's product filter out of the jsonb blob. Null means
        /// "any pass product", which is also what a malformed blob degrades to.</summary>
        internal static Guid? FromProductId(MarketingAutomation a)
        {
            if (string.IsNullOrWhiteSpace(a.TriggerConfig)) return null;
            try
            {
                using var doc = JsonDocument.Parse(a.TriggerConfig);
                if (doc.RootElement.TryGetProperty("fromProductId", out var el)
                    && el.ValueKind == JsonValueKind.String
                    && Guid.TryParse(el.GetString(), out var id))
                {
                    return id;
                }
            }
            catch (JsonException) { /* treated as "any product" */ }
            return null;
        }

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
