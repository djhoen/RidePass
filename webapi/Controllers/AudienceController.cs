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
    /// Saved audiences: a base list plus rules, evaluated live wherever they are used (a campaign
    /// send, an automation trigger). Sits beside <see cref="CampaignController"/> and
    /// <see cref="AutomationController"/> on the Email page.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
    public class AudienceController : ControllerBase
    {
        private const int MaxRules = 20;
        private const int MaxValues = 200;

        private readonly IAudienceRepository _audiences;
        private readonly ICampaignAudienceRepository _targets;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ITenantContext _tenantContext;

        public AudienceController(
            IAudienceRepository audiences,
            ICampaignAudienceRepository targets,
            IEmailSuppressionRepository suppression,
            ITenantContext tenantContext)
        {
            _audiences = audiences;
            _targets = targets;
            _suppression = suppression;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            var rows = await _audiences.ListForTenant(tenantId);
            var usage = await _audiences.UsageCounts(tenantId);
            // Stored counts: refreshed hourly by the sweep and whenever an audience is saved.
            // Evaluating every audience live here made the list take tens of seconds.
            var members = await _audiences.ActiveMemberCounts(tenantId);
            var names = await NameLookup();
            var items = new List<AudienceItem>();
            foreach (var a in rows)
            {
                var def = AudienceDefinition.Parse(a.Definition);
                var item = ToItem(a, def, names);
                item.MemberCount = members.GetValueOrDefault(a.Id);
                if (usage.TryGetValue(a.Id, out var u))
                {
                    item.UsedByCampaigns = u.Campaigns;
                    item.UsedByAutomations = u.Automations;
                }
                items.Add(item);
            }
            return new ApiResponses().OkResult(items);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var a = await _audiences.GetById(id, _tenantContext.TenantId);
            if (a is null) return new ApiResponses().NotFoundResult("Audience not found.");
            var def = AudienceDefinition.Parse(a.Definition);
            var item = ToItem(a, def, await NameLookup());
            item.MemberCount = await _audiences.Count(_tenantContext.TenantId, def);
            return new ApiResponses().OkResult(item);
        }

        /// <summary>The events, event types, and pass products the rule builder can pick from.</summary>
        [HttpGet("Options")]
        public async Task<IActionResult> Options()
        {
            var o = await _targets.ListOptions(_tenantContext.TenantId);
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

        /// <summary>Live count and a few names for a definition that may not be saved yet.</summary>
        [HttpPost("Preview")]
        public async Task<IActionResult> Preview([FromBody] AudiencePreviewRequest request)
        {
            var (def, error) = Validate(request.Definition);
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            var people = await _audiences.Evaluate(_tenantContext.TenantId, def);
            var blocklist = await _suppression.ListMarketingBlocklist(_tenantContext.TenantId);
            return new ApiResponses().OkResult(new AudiencePreviewResponse
            {
                Count = people.Count,
                Suppressed = people.Count(r => blocklist.Contains(r.Email)),
                Summary = Summarize(def, await NameLookup()),
                Sample = people.Take(5).Select(r => new AudiencePreviewPerson { Email = r.Email, Name = r.Name }).ToList(),
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertAudienceRequest request)
        {
            var (def, error) = Validate(request.Definition);
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            var a = new Audience
            {
                TenantId = _tenantContext.TenantId,
                Name = request.Name.Trim(),
                Description = Trim(request.Description),
                Definition = def.ToJson(),
                CreatedByUserId = TryGetUserId(out var uid) ? uid : null,
            };
            try
            {
                a.Id = await _audiences.Create(a);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return new ApiResponses().BadRequestResult($"You already have an audience called \"{a.Name}\". Pick another name.");
            }
            // Membership starts now, so an automation armed later sees only new arrivals.
            await _audiences.RefreshMembers(a);
            return await Get(a.Id);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertAudienceRequest request)
        {
            var existing = await _audiences.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Audience not found.");
            var (def, error) = Validate(request.Definition);
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            existing.Name = request.Name.Trim();
            existing.Description = Trim(request.Description);
            existing.Definition = def.ToJson();
            try
            {
                await _audiences.Update(existing);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return new ApiResponses().BadRequestResult($"You already have an audience called \"{existing.Name}\". Pick another name.");
            }
            await _audiences.RefreshMembers(existing);
            return await Get(id);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _audiences.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Audience not found.");
            var usage = (await _audiences.UsageCounts(_tenantContext.TenantId)).GetValueOrDefault(id);
            if (usage is not null && (usage.Campaigns > 0 || usage.Automations > 0))
            {
                var parts = new List<string>();
                if (usage.Campaigns > 0) parts.Add($"{usage.Campaigns} unsent campaign{(usage.Campaigns == 1 ? "" : "s")}");
                if (usage.Automations > 0) parts.Add($"{usage.Automations} automation{(usage.Automations == 1 ? "" : "s")}");
                return new ApiResponses().BadRequestResult(
                    $"\"{existing.Name}\" is used by {string.Join(" and ", parts)}. Point them at another audience first.");
            }
            await _audiences.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { deleted = true });
        }

        /// <summary>Add the starter audiences this track does not have yet.</summary>
        [HttpPost("Samples")]
        public async Task<IActionResult> Samples()
        {
            var added = await _audiences.SeedSamples(_tenantContext.TenantId, TryGetUserId(out var uid) ? uid : null);
            // New samples get their membership stamped now, same as a hand-made audience.
            foreach (var a in await _audiences.ListForTenant(_tenantContext.TenantId))
            {
                if (a.IsSample) await _audiences.RefreshMembers(a);
            }
            return new ApiResponses().OkResult(new { added });
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static (AudienceDefinition Def, string? Error) Validate(AudienceDefinitionDto dto)
        {
            var def = new AudienceDefinition
            {
                Base = (dto.Base ?? "").Trim().ToLowerInvariant(),
                Match = string.Equals(dto.Match, "any", StringComparison.OrdinalIgnoreCase) ? "any" : "all",
            };
            if (!AudienceBases.IsValid(def.Base))
            {
                return (def, "Start from everyone, newsletter subscribers, or customers.");
            }
            if (dto.Rules.Count > MaxRules) return (def, $"Keep it to {MaxRules} filters.");
            foreach (var r in dto.Rules)
            {
                var kind = (r.Kind ?? "").Trim().ToLowerInvariant();
                if (!AudienceRuleKinds.IsValid(kind)) return (def, "One of the filters is of a kind this version does not know.");
                var rule = new AudienceRule
                {
                    Kind = kind,
                    Negate = r.Negate,
                    Ids = r.Ids.Distinct().Take(MaxValues).ToList(),
                    Values = r.Values.Select(v => (v ?? "").Trim()).Where(v => v.Length > 0 && v.Length <= 80).Distinct().Take(MaxValues).ToList(),
                    Days = r.Days,
                    ActiveOnly = r.ActiveOnly,
                    FromUtc = r.FromUtc?.ToUniversalTime(),
                    ToUtc = r.ToUtc?.ToUniversalTime(),
                };
                switch (kind)
                {
                    case AudienceRuleKinds.EventTypePurchased when rule.Ids.Count == 0:
                        return (def, "Pick at least one event type for the event type filter.");
                    case AudienceRuleKinds.PostalCode or AudienceRuleKinds.State or AudienceRuleKinds.City when rule.Values.Count == 0:
                        return (def, "The address filter needs at least one value.");
                    case AudienceRuleKinds.PassExpiring when rule.Days is null or < 0 or > 3650:
                        return (def, "Say how many days ahead the pass-ending filter looks (0 to 3650).");
                    case AudienceRuleKinds.AbandonedCart when rule.Days is null or < 1 or > 365:
                        return (def, "Say how many days back the unfinished-checkout filter looks (1 to 365).");
                }
                if (rule.FromUtc is not null && rule.ToUtc is not null && rule.ToUtc <= rule.FromUtc)
                {
                    return (def, "An event date range is backwards: the end must be after the start.");
                }
                def.Rules.Add(rule);
            }
            return (def, null);
        }

        private async Task<Func<Guid, string?>> NameLookup()
        {
            var o = await _targets.ListOptions(_tenantContext.TenantId);
            var map = new Dictionary<Guid, string>();
            foreach (var e in o.Events) map[e.Id] = e.Title;
            foreach (var t in o.EventTypes) map[t.Id] = t.Name;
            foreach (var p in o.PassProducts) map[p.Id] = p.Name;
            return id => map.TryGetValue(id, out var n) ? n : null;
        }

        private static string Summarize(AudienceDefinition def, Func<Guid, string?> nameOf)
        {
            if (def.Rules.Count == 0) return AudienceQuery.DescribeBase(def.Base);
            var joiner = def.Match == "any" ? " or " : " and ";
            return $"{AudienceQuery.DescribeBase(def.Base)} who {string.Join(joiner, def.Rules.Select(r => AudienceQuery.Describe(r, nameOf)))}";
        }

        private static AudienceItem ToItem(Audience a, AudienceDefinition def, Func<Guid, string?> nameOf) => new()
        {
            Id = a.Id,
            Name = a.Name,
            Description = a.Description,
            IsSample = a.IsSample,
            Summary = Summarize(def, nameOf),
            UpdatedAt = a.UpdatedAt,
            Definition = new AudienceDefinitionDto
            {
                Base = def.Base,
                Match = def.Match,
                Rules = def.Rules.Select(r => new AudienceRuleDto
                {
                    Kind = r.Kind, Negate = r.Negate, Ids = r.Ids, Values = r.Values, Days = r.Days,
                    ActiveOnly = r.ActiveOnly, FromUtc = r.FromUtc, ToUtc = r.ToUtc,
                    Label = AudienceQuery.Describe(r, nameOf),
                }).ToList(),
            },
        };

        private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }
    }
}
