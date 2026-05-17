using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Tenant Admin → Customers. Every endpoint is gated by CustomersView and tenant-
    // scoped via _tenantContext + the CustomerRepository's activity-based filtering.
    // A "customer" is any user who has paid at this tenant or signed a waiver here —
    // the User.tenant_id column (where the user originally registered) is irrelevant.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
    public class CustomerController : ControllerBase
    {
        private readonly ICustomerRepository _customers;
        private readonly ITenantContext _tenantContext;

        public CustomerController(ICustomerRepository customers, ITenantContext tenantContext)
        {
            _customers = customers;
            _tenantContext = tenantContext;
        }

        // GET /api/Customer?search=&limit=&offset=
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? search = null,
            [FromQuery] int limit = 50,
            [FromQuery] int offset = 0)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved for this request.");
            }
            if (limit < 1 || limit > 200) limit = 50;
            if (offset < 0) offset = 0;

            var rows = await _customers.ListForTenant(_tenantContext.TenantId, search, limit, offset);
            var total = await _customers.CountForTenant(_tenantContext.TenantId, search);

            return new ApiResponses().OkResult(new { Items = rows, Total = total, Limit = limit, Offset = offset });
        }

        // GET /api/Customer/{userId}
        [HttpGet("{userId:guid}")]
        public async Task<IActionResult> Detail(Guid userId)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved for this request.");
            }

            var detail = await _customers.GetDetail(userId, _tenantContext.TenantId);
            if (detail == null)
            {
                // Repository returns null when the user has no activity at this tenant.
                // We deliberately return 404 (not 403) so we don't confirm the user
                // exists outside this tenant's scope.
                return new ApiResponses().NotFoundResult("Customer not found.");
            }
            return new ApiResponses().OkResult(detail);
        }

        // GET /api/Customer/top-riders?metric=days&period=month&limit=10
        // metric: "days" | "spent"  (defaults to "days")
        // period: "month" | "year"  (defaults to "month")
        [HttpGet("top-riders")]
        public async Task<IActionResult> TopRiders(
            [FromQuery] string? metric = "days",
            [FromQuery] string? period = "month",
            [FromQuery] int limit = 10)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved for this request.");
            }
            var safeMetric = metric == "spent" ? "spent" : "days";
            var safePeriod = period == "year" ? "year" : "month";
            if (limit < 1 || limit > 50) limit = 10;

            var rows = await _customers.GetTopRiders(_tenantContext.TenantId, safeMetric, safePeriod, limit);
            return new ApiResponses().OkResult(rows);
        }
    }
}
