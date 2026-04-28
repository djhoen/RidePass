using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Blackout;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class BlackoutController : ControllerBase
    {
        private readonly IBlackoutRepository _blackouts;
        private readonly ITenantContext _tenantContext;

        public BlackoutController(IBlackoutRepository blackouts, ITenantContext tenantContext)
        {
            _blackouts = blackouts;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetInRange([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved for this request.");
            }

            if (toUtc <= fromUtc)
            {
                return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");
            }

            var rows = await _blackouts.GetInRange(_tenantContext.TenantId, fromUtc.ToUniversalTime(), toUtc.ToUniversalTime());
            return new ApiResponses().OkResult(rows.Select(MapResponse));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertBlackoutRequest request)
        {
            if (request.EndsAtUtc < request.StartsAtUtc)
            {
                return new ApiResponses().BadRequestResult("EndsAt must be on or after StartsAt.");
            }

            var row = new Services.Repositories.Data.EventData.Blackout
            {
                TenantId = _tenantContext.TenantId,
                StartsAt = request.StartsAtUtc.ToUniversalTime(),
                EndsAt = request.EndsAtUtc.ToUniversalTime(),
                AllDay = request.AllDay,
                Reason = request.Reason,
            };
            row.Id = await _blackouts.Create(row);
            return new ApiResponses().OkResult(MapResponse(row));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertBlackoutRequest request)
        {
            var existing = await _blackouts.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Blackout not found.");
            }

            if (request.EndsAtUtc < request.StartsAtUtc)
            {
                return new ApiResponses().BadRequestResult("EndsAt must be on or after StartsAt.");
            }

            existing.StartsAt = request.StartsAtUtc.ToUniversalTime();
            existing.EndsAt = request.EndsAtUtc.ToUniversalTime();
            existing.AllDay = request.AllDay;
            existing.Reason = request.Reason;

            await _blackouts.Update(existing);
            return new ApiResponses().OkResult(MapResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _blackouts.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Blackout not found.");
            }

            await _blackouts.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        private static BlackoutResponse MapResponse(Services.Repositories.Data.EventData.Blackout row) => new()
        {
            Id = row.Id,
            StartsAtUtc = DateTime.SpecifyKind(row.StartsAt, DateTimeKind.Utc),
            EndsAtUtc = DateTime.SpecifyKind(row.EndsAt, DateTimeKind.Utc),
            AllDay = row.AllDay,
            Reason = row.Reason,
        };
    }
}
