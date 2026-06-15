using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Suppression;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Tenant-admin view of the email suppression (do-not-send) list. Scoped to the tenant's
    /// own rows; platform-wide hard bounces are intentionally not surfaced here (they can carry
    /// addresses from other tenants' sends). Same CampaignsManage policy as the newsletter
    /// and campaign screens.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
    public class SuppressionController : ControllerBase
    {
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ITenantContext _tenantContext;

        public SuppressionController(IEmailSuppressionRepository suppression, ITenantContext tenantContext)
        {
            _suppression = suppression;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }
            var rows = await _suppression.ListForTenant(_tenantContext.TenantId);
            var items = rows.Select(r => new SuppressionListItem
            {
                Id = r.Id,
                Email = r.Email,
                Reason = r.Reason,
                Scope = r.Scope,
                Source = r.Source,
                Detail = r.Detail,
                CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
            });
            return new ApiResponses().OkResult(items);
        }

        // Manually suppress an address for this tenant (admin knows the person doesn't want mail).
        // Marketing scope: receipts/verification still reach them, only promotional mail stops.
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] AddSuppressionRequest request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }
            var email = (request.Email ?? "").Trim();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                return new ApiResponses().BadRequestResult("A valid email address is required.");
            }
            await _suppression.Suppress(_tenantContext.TenantId, email, "manual", "marketing", "admin", request.Note);
            return new ApiResponses().OkResult(new { added = true });
        }

        // Re-enable an address by removing the tenant's suppression for it.
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Remove(Guid id)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }
            await _suppression.RemoveForTenant(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { removed = true });
        }
    }
}
