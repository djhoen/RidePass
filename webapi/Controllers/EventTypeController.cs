using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.EventType;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EventTypeController : ControllerBase
    {
        private readonly ITenantEventTypeRepository _repo;
        private readonly ITenantContext _tenantContext;

        public EventTypeController(ITenantEventTypeRepository repo, ITenantContext tenantContext)
        {
            _repo = repo;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved for this request.");
            }

            var rows = await _repo.GetAllForTenant(_tenantContext.TenantId);
            return new ApiResponses().OkResult(rows.Select(ToResponse));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertEventTypeRequest request)
        {
            var type = new TenantEventType
            {
                TenantId = _tenantContext.TenantId,
                Code = $"custom_{Guid.NewGuid():N}",
                Name = request.Name,
                Color = request.Color,
                SortOrder = request.SortOrder,
                IsSystem = false,
            };

            var id = await _repo.Create(type);
            type.Id = id;
            return new ApiResponses().OkResult(ToResponse(type));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertEventTypeRequest request)
        {
            var existing = await _repo.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Event type not found.");
            }

            await _repo.Update(id, _tenantContext.TenantId, request.Name, request.Color, request.SortOrder);
            existing.Name = request.Name;
            existing.Color = request.Color;
            existing.SortOrder = request.SortOrder;
            return new ApiResponses().OkResult(ToResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _repo.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Event type not found.");
            }

            if (existing.IsSystem)
            {
                return new ApiResponses().BadRequestResult("System event types cannot be deleted (rename or recolor instead).");
            }

            if (await _repo.IsInUseByEvents(id, _tenantContext.TenantId))
            {
                return new ApiResponses().BadRequestResult("This event type is in use by one or more events and cannot be deleted.");
            }

            await _repo.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        private static EventTypeResponse ToResponse(TenantEventType row) => new()
        {
            Id = row.Id,
            Code = row.Code,
            Name = row.Name,
            Color = row.Color,
            SortOrder = row.SortOrder,
            IsSystem = row.IsSystem,
        };
    }
}
