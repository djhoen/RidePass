using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.EventTicketTier;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/Event/{eventId:guid}/Tiers")]
    public class EventTicketTierController : ControllerBase
    {
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventRepository _events;
        private readonly ITenantContext _tenantContext;

        public EventTicketTierController(
            IEventTicketTierRepository tiers,
            IEventRepository events,
            ITenantContext tenantContext)
        {
            _tiers = tiers;
            _events = events;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetForEvent(Guid eventId)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var rows = await _tiers.GetForEvent(eventId, _tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(rows.Select(r => ToResponse(r, sold: null)));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Admin")]
        public async Task<IActionResult> GetAllForAdmin(Guid eventId)
        {
            var rows = await _tiers.GetForEvent(eventId, _tenantContext.TenantId, activeOnly: false);
            var responses = new List<EventTicketTierResponse>();
            foreach (var r in rows)
            {
                var sold = await _tiers.SoldCount(r.Id);
                responses.Add(ToResponse(r, sold));
            }
            return new ApiResponses().OkResult(responses);
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost]
        public async Task<IActionResult> Create(Guid eventId, [FromBody] UpsertEventTicketTierRequest request)
        {
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null)
            {
                return new ApiResponses().NotFoundResult("Event not found.");
            }

            var tier = new EventTicketTier
            {
                TenantId = _tenantContext.TenantId,
                EventId = eventId,
                Name = request.Name,
                PriceCents = request.PriceCents,
                Inventory = request.Inventory,
                SortOrder = request.SortOrder,
                IsActive = request.IsActive,
            };
            tier.Id = await _tiers.Create(tier);
            return new ApiResponses().OkResult(ToResponse(tier, sold: 0));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid eventId, Guid id, [FromBody] UpsertEventTicketTierRequest request)
        {
            var existing = await _tiers.GetById(id, _tenantContext.TenantId);
            if (existing is null || existing.EventId != eventId)
            {
                return new ApiResponses().NotFoundResult("Tier not found.");
            }

            existing.Name = request.Name;
            existing.PriceCents = request.PriceCents;
            existing.Inventory = request.Inventory;
            existing.SortOrder = request.SortOrder;
            existing.IsActive = request.IsActive;

            await _tiers.Update(existing);
            var sold = await _tiers.SoldCount(id);
            return new ApiResponses().OkResult(ToResponse(existing, sold));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid eventId, Guid id)
        {
            var existing = await _tiers.GetById(id, _tenantContext.TenantId);
            if (existing is null || existing.EventId != eventId)
            {
                return new ApiResponses().NotFoundResult("Tier not found.");
            }

            var sold = await _tiers.SoldCount(id);
            if (sold > 0)
            {
                return new ApiResponses().BadRequestResult("This tier has purchases and cannot be deleted. Set inactive instead.");
            }

            await _tiers.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        private static EventTicketTierResponse ToResponse(EventTicketTier t, int? sold) => new()
        {
            Id = t.Id,
            EventId = t.EventId,
            Name = t.Name,
            PriceCents = t.PriceCents,
            Inventory = t.Inventory,
            Sold = sold,
            SortOrder = t.SortOrder,
            IsActive = t.IsActive,
        };
    }
}
