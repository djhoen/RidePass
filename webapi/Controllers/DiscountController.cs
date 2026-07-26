using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.DiscountData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Discount;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Tenant-defined discounts a staff member applies at a counter ("Military 10%", "VMBA member").
    //
    // Managing them is SettingsManage: deciding what the track gives away, and whether taking it
    // needs a manager, is a policy decision rather than an operational one. READING the list for a
    // surface is open to any counter permission, because a cashier has to be able to see the
    // buttons they are allowed to press.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DiscountController : ControllerBase
    {
        private readonly IDiscountPresetRepository _discounts;
        private readonly ITenantRepository _tenants;
        private readonly ITenantContext _tenantContext;

        public DiscountController(IDiscountPresetRepository discounts, ITenantRepository tenants,
            ITenantContext tenantContext)
        {
            _discounts = discounts;
            _tenants = tenants;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;

        private static string Label(DiscountPreset p) =>
            p.Kind == "percent"
                ? $"{p.Value / 100m:0.##}% off"
                : $"${p.Value / 100m:0.00} off";

        private static DiscountPresetResponse Map(DiscountPreset p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Kind = p.Kind,
            Value = p.Value,
            Surfaces = p.Surfaces,
            RequiresManager = p.RequiresManager,
            IsActive = p.IsActive,
            SortOrder = p.SortOrder,
            Label = Label(p),
        };

        /// <summary>Validates the request and returns an error message, or null when it's good.</summary>
        private static string? Validate(UpsertDiscountPresetRequest req, out string[] surfaces)
        {
            surfaces = (req.Surfaces ?? new List<string>())
                .Select(s => (s ?? string.Empty).Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (surfaces.Length == 0)
                return "Choose at least one place this discount applies.";

            var unknown = surfaces.FirstOrDefault(s => !DiscountSurfaces.All.Contains(s));
            if (unknown is not null)
                return $"\"{unknown}\" isn't somewhere a discount can be applied.";

            // Range-checked per kind because the two units share nothing: 1000 is a sensible
            // percent (10%) and a sensible amount ($10), but 20000 is neither a valid percent nor
            // an obviously wrong amount, so one shared range would let a bad percent through.
            if (req.Kind == "percent" && (req.Value < 1 || req.Value > 10_000))
                return "A percentage discount must be between 0.01% and 100%.";
            if (req.Kind == "amount" && req.Value < 1)
                return "A fixed discount must be at least one cent.";

            return null;
        }

        /// <summary>Whether several discounts may combine on one sale. Off by default, in which
        /// case the largest single discount applies. Lives here rather than with the general tenant
        /// settings because it is a discount policy, and it is the screen an admin is already on
        /// when they think about it.</summary>
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Stacking")]
        public async Task<IActionResult> SetStacking([FromBody] SetDiscountStackingRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _tenants.UpdateAllowDiscountStacking(TenantId, req.Allow);
            return new ApiResponses().OkResult();
        }

        /// <summary>Every discount for the settings screen, active and inactive.</summary>
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _discounts.ListForTenant(TenantId, activeOnly: false);
            return new ApiResponses().OkResult(rows.Select(Map));
        }

        /// <summary>The active discounts a given counter may offer. Any counter permission
        /// qualifies: a cashier needs to see the buttons they're allowed to press, and this
        /// returns configuration rather than customer data.</summary>
        [Authorize(Policy = TenantPermissions.Policy.AnyCounter)]
        [HttpGet("For/{surface}")]
        public async Task<IActionResult> ForSurface(string surface)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!DiscountSurfaces.All.Contains(surface))
                return new ApiResponses().BadRequestResult("Unknown discount surface.");
            var rows = await _discounts.ListForSurface(TenantId, surface);
            return new ApiResponses().OkResult(rows.Select(Map));
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertDiscountPresetRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (Validate(req, out var surfaces) is { } error) return new ApiResponses().BadRequestResult(error);

            var id = await _discounts.Create(new DiscountPreset
            {
                TenantId = TenantId,
                Name = req.Name.Trim(),
                Kind = req.Kind,
                Value = req.Value,
                Surfaces = surfaces,
                RequiresManager = req.RequiresManager,
                IsActive = req.IsActive,
                SortOrder = req.SortOrder,
            });
            return new ApiResponses().OkResult(new { id });
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertDiscountPresetRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (Validate(req, out var surfaces) is { } error) return new ApiResponses().BadRequestResult(error);

            // Tenant scope is in the UPDATE's own WHERE, so a spoofed id from another tenant
            // affects nothing and reports not-found rather than silently succeeding.
            var affected = await _discounts.Update(new DiscountPreset
            {
                Id = id,
                TenantId = TenantId,
                Name = req.Name.Trim(),
                Kind = req.Kind,
                Value = req.Value,
                Surfaces = surfaces,
                RequiresManager = req.RequiresManager,
                IsActive = req.IsActive,
                SortOrder = req.SortOrder,
            });
            return affected == 0
                ? new ApiResponses().NotFoundResult("Discount not found.")
                : new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var affected = await _discounts.Delete(id, TenantId);
            return affected == 0
                ? new ApiResponses().NotFoundResult("Discount not found.")
                : new ApiResponses().OkResult();
        }
    }
}
