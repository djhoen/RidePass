using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Shop inventory reporting: valuation, sales/COGS/margin, dead stock. Both the shop admin
    // (CatalogManage) and the money people (ReportsView, which accountants hold) can read these;
    // [Authorize(Policy=...)] can only AND, so the OR is checked by hand per action.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BikeShopReportController : ControllerBase
    {
        private readonly IBikeShopRepository _shop;
        private readonly ITenantContext _tenantContext;

        public BikeShopReportController(IBikeShopRepository shop, ITenantContext tenantContext)
        {
            _shop = shop;
            _tenantContext = tenantContext;
        }

        private IActionResult? Gate()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var roles = User.FindAll("role").Select(c => c.Value).ToList();
            var perms = TenantPermissions.ForRoles(roles);
            if (!roles.Contains("super_admin")
                && !perms.Contains(TenantPermissions.CatalogManage)
                && !perms.Contains(TenantPermissions.ReportsView))
                return Forbid();
            return null;
        }

        [HttpGet("Valuation")]
        public async Task<IActionResult> Valuation()
        {
            if (Gate() is { } blocked) return blocked;
            return new ApiResponses().OkResult(await _shop.GetValuationReport(_tenantContext.TenantId));
        }

        // Defaults to the last 30 days. Revenue is discounted pre-tax goods; COGS prefers the
        // per-line cost snapshot and falls back to current cost for pre-snapshot history.
        [HttpGet("Sales")]
        public async Task<IActionResult> Sales([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc)
        {
            if (Gate() is { } blocked) return blocked;
            var to = toUtc ?? DateTime.UtcNow;
            var from = fromUtc ?? to.AddDays(-30);
            if (from >= to) return new ApiResponses().BadRequestResult("The start of the range must be before the end.");
            return new ApiResponses().OkResult(new
            {
                fromUtc = from,
                toUtc = to,
                rows = await _shop.GetSalesReport(_tenantContext.TenantId, from, to),
            });
        }

        // Estimated-vs-actual labor time per job, for jobs with any time recorded in the range.
        [HttpGet("LaborTime")]
        public async Task<IActionResult> LaborTime([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc)
        {
            if (Gate() is { } blocked) return blocked;
            var to = toUtc ?? DateTime.UtcNow;
            var from = fromUtc ?? to.AddDays(-30);
            if (from >= to) return new ApiResponses().BadRequestResult("The start of the range must be before the end.");
            return new ApiResponses().OkResult(new
            {
                fromUtc = from,
                toUtc = to,
                rows = await _shop.GetLaborTimeReport(_tenantContext.TenantId, from, to),
            });
        }

        [HttpGet("DeadStock")]
        public async Task<IActionResult> DeadStock([FromQuery] int days = 60)
        {
            if (Gate() is { } blocked) return blocked;
            days = Math.Clamp(days, 7, 730);
            return new ApiResponses().OkResult(new
            {
                days,
                rows = await _shop.GetDeadStockReport(_tenantContext.TenantId, DateTime.UtcNow.AddDays(-days)),
            });
        }
    }
}
