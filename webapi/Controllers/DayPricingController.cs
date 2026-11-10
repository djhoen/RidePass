using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Repositories.Data.PricingData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.DayPricing;
using Services.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Peak days (Script0316, Highland go-live item 13): weekdays or dates when a day pass costs more.
    /// Applied by ITierDayPricer to every sale and price listing. Catalog managers own prices.
    /// </summary>
    [ApiController]
    [Route("api/DayPricing")]
    [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
    public class DayPricingController : ControllerBase
    {
        private readonly IDayPriceRuleRepository _rules;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ITenantContext _tenantContext;

        public DayPricingController(IDayPriceRuleRepository rules, Services.Audit.IAuditLogger audit, ITenantContext tenantContext)
        {
            _rules = rules;
            _audit = audit;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _rules.List(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(rows.Select(ToItem).ToList());
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDayPriceRuleRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(request.Label))
                return new ApiResponses().BadRequestResult("Name the peak day (e.g. Saturday, July 4th).");
            if ((request.Weekday is null) == (request.OnDate is null))
                return new ApiResponses().BadRequestResult("Pick a weekday or a date.");
            if (request.AdjustKind is not ("amount" or "percent") || request.AdjustValue < 1)
                return new ApiResponses().BadRequestResult("Give how much more a day pass costs that day.");
            if (request.AdjustKind == "percent" && request.AdjustValue > 100_000)
                return new ApiResponses().BadRequestResult("A peak day can add up to 1000%.");
            Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId);

            var id = await _rules.Create(new DayPriceRule
            {
                TenantId = _tenantContext.TenantId,
                Label = request.Label.Trim(),
                Weekday = request.Weekday,
                OnDate = request.OnDate?.ToDateTime(TimeOnly.MinValue),
                EventTypeId = request.EventTypeId,
                AdjustKind = request.AdjustKind,
                AdjustValue = request.AdjustValue,
                CreatedByUserId = userId == Guid.Empty ? null : userId,
            });
            if (id == Guid.Empty) return new ApiResponses().BadRequestResult("That event type isn't one of this venue's.");
            await _audit.Log("pricing.day_rule.create", $"Added peak-day price \"{request.Label.Trim()}\"",
                targetKind: "day_price_rule", targetId: id, tenantId: _tenantContext.TenantId,
                metadata: new { request.Weekday, request.OnDate, request.EventTypeId, request.AdjustKind, request.AdjustValue });
            return new ApiResponses().OkResult(new { id });
        }

        [HttpPut("{id:guid}/Active")]
        public async Task<IActionResult> SetActive(Guid id, [FromBody] SetDayPriceRuleActiveRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (await _rules.SetActive(_tenantContext.TenantId, id, request.IsActive) == 0)
                return new ApiResponses().NotFoundResult("Peak day not found.");
            await _audit.Log("pricing.day_rule.active", $"{(request.IsActive ? "Turned on" : "Turned off")} a peak-day price",
                targetKind: "day_price_rule", targetId: id, tenantId: _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { id, request.IsActive });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (await _rules.Delete(_tenantContext.TenantId, id) == 0)
                return new ApiResponses().NotFoundResult("Peak day not found.");
            await _audit.Log("pricing.day_rule.delete", "Removed a peak-day price",
                targetKind: "day_price_rule", targetId: id, tenantId: _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { id });
        }

        private static DayPriceRuleItem ToItem(DayPriceRule r) => new()
        {
            Id = r.Id,
            Label = r.Label,
            Weekday = r.Weekday,
            OnDate = r.OnDate is DateTime d ? DateOnly.FromDateTime(d) : null,
            EventTypeId = r.EventTypeId,
            AdjustKind = r.AdjustKind,
            AdjustValue = r.AdjustValue,
            IsActive = r.IsActive,
        };
    }
}
