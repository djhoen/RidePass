using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.EventData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Event;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EventController : ControllerBase
    {
        private readonly IEventRepository _events;
        private readonly ITenantEventTypeRepository _eventTypes;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IDayPassPurchaseRepository _dayPasses;
        private readonly ITenantContext _tenantContext;

        public EventController(
            IEventRepository events,
            ITenantEventTypeRepository eventTypes,
            IEventTicketTierRepository tiers,
            IDayPassPurchaseRepository dayPasses,
            ITenantContext tenantContext)
        {
            _events = events;
            _eventTypes = eventTypes;
            _tiers = tiers;
            _dayPasses = dayPasses;
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

            var events = await _events.GetInRange(_tenantContext.TenantId, fromUtc.ToUniversalTime(), toUtc.ToUniversalTime());
            var types = (await _eventTypes.GetAllForTenant(_tenantContext.TenantId)).ToDictionary(t => t.Id);
            var tiersByEvent = await _tiers.GetForEvents(events.Select(e => e.Id), _tenantContext.TenantId, activeOnly: true);

            // For events with capacity, fetch current reserved count so the UI can show spots left.
            var reservableIds = events.Where(e => e.Capacity.HasValue).Select(e => e.Id).ToList();
            var reservedByEvent = await _dayPasses.ActiveSpotsReservedForEvents(reservableIds);

            var response = events.Select(ev =>
            {
                var r = MapResponse(ev, types);
                if (tiersByEvent.TryGetValue(ev.Id, out var tiers) && tiers.Count > 0)
                {
                    r.HasActiveTiers = true;
                    r.MinTicketPriceCents = tiers.Min(t => t.PriceCents);
                }
                if (ev.Capacity.HasValue)
                {
                    r.SpotsReserved = reservedByEvent.TryGetValue(ev.Id, out var reserved) ? reserved : 0;
                }
                return r;
            }).ToList();
            return new ApiResponses().OkResult(response);
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertEventRequest request)
        {
            var typeCheck = await _eventTypes.GetById(request.EventTypeId, _tenantContext.TenantId);
            if (typeCheck is null)
            {
                return new ApiResponses().BadRequestResult("Invalid event type for this tenant.");
            }

            if (request.EndsAtUtc < request.StartsAtUtc)
            {
                return new ApiResponses().BadRequestResult("EndsAt must be on or after StartsAt.");
            }

            var ev = new Event
            {
                TenantId = _tenantContext.TenantId,
                EventTypeId = request.EventTypeId,
                Title = request.Title,
                Description = request.Description,
                StartsAt = request.StartsAtUtc.ToUniversalTime(),
                EndsAt = request.EndsAtUtc.ToUniversalTime(),
                AllDay = request.AllDay,
                Capacity = request.Capacity,
                LocationLabel = request.LocationLabel,
                Status = request.Status,
            };

            ev.Id = await _events.Create(ev);
            var types = new Dictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType> { [typeCheck.Id] = typeCheck };
            return new ApiResponses().OkResult(MapResponse(ev, types));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertEventRequest request)
        {
            var existing = await _events.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Event not found.");
            }

            var typeCheck = await _eventTypes.GetById(request.EventTypeId, _tenantContext.TenantId);
            if (typeCheck is null)
            {
                return new ApiResponses().BadRequestResult("Invalid event type for this tenant.");
            }

            if (request.EndsAtUtc < request.StartsAtUtc)
            {
                return new ApiResponses().BadRequestResult("EndsAt must be on or after StartsAt.");
            }

            existing.EventTypeId = request.EventTypeId;
            existing.Title = request.Title;
            existing.Description = request.Description;
            existing.StartsAt = request.StartsAtUtc.ToUniversalTime();
            existing.EndsAt = request.EndsAtUtc.ToUniversalTime();
            existing.AllDay = request.AllDay;
            existing.Capacity = request.Capacity;
            existing.LocationLabel = request.LocationLabel;
            existing.Status = request.Status;

            await _events.Update(existing);
            var types = new Dictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType> { [typeCheck.Id] = typeCheck };
            return new ApiResponses().OkResult(MapResponse(existing, types));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _events.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Event not found.");
            }

            await _events.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("{id:guid}/Duplicate")]
        public async Task<IActionResult> Duplicate(Guid id)
        {
            var source = await _events.GetById(id, _tenantContext.TenantId);
            if (source is null)
            {
                return new ApiResponses().NotFoundResult("Event not found.");
            }

            var shift = TimeSpan.FromDays(7);
            var clone = new Event
            {
                TenantId = source.TenantId,
                EventTypeId = source.EventTypeId,
                Title = source.Title,
                Description = source.Description,
                StartsAt = source.StartsAt + shift,
                EndsAt = source.EndsAt + shift,
                AllDay = source.AllDay,
                Capacity = source.Capacity,
                LocationLabel = source.LocationLabel,
                Status = "scheduled",
            };
            clone.Id = await _events.Create(clone);

            var type = await _eventTypes.GetById(source.EventTypeId, _tenantContext.TenantId);
            var types = type is null
                ? new Dictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType>()
                : new Dictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType> { [type.Id] = type };
            return new ApiResponses().OkResult(MapResponse(clone, types));
        }

        private static EventResponse MapResponse(Event ev, IReadOnlyDictionary<Guid, Services.Repositories.Data.TenantData.TenantEventType> types)
        {
            types.TryGetValue(ev.EventTypeId, out var type);
            return new EventResponse
            {
                Id = ev.Id,
                EventTypeId = ev.EventTypeId,
                EventTypeCode = type?.Code ?? string.Empty,
                EventTypeName = type?.Name ?? string.Empty,
                EventTypeColor = type?.Color ?? "#616161",
                Title = ev.Title,
                Description = ev.Description,
                StartsAtUtc = DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc),
                EndsAtUtc = DateTime.SpecifyKind(ev.EndsAt, DateTimeKind.Utc),
                AllDay = ev.AllDay,
                Capacity = ev.Capacity,
                LocationLabel = ev.LocationLabel,
                Status = ev.Status,
            };
        }
    }
}
