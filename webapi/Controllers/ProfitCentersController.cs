using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Accounting;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.ProfitCenters;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Tenant-configurable profit centers: named revenue buckets ("Corp Tickets", "Training
    /// Center") that group the QuickBooks revenue slots for reporting and for the QuickBooks
    /// account-mapping screen. The journal entry still posts per slot; centers only decide how
    /// slots are grouped and labeled, so nothing here can unbalance a posted day.
    ///
    /// AccountingManage throughout, the same permission as the QuickBooks settings page these
    /// centers feed: whoever may point money at ledger accounts may also regroup it.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ProfitCentersController : ControllerBase
    {
        // The DB CHECK chk_tenant_event_type_revenue_key (Script0274) allows exactly these; keep
        // in lockstep when widening the CHECK.
        private static readonly string[] EventRoutingKeys =
        {
            QboAccountKeys.RevenueEventTicket,
            QboAccountKeys.RevenueTraining,
        };

        private static IEnumerable<string> RevenueKeys =>
            QboAccountKeys.All.Where(k => k.StartsWith("revenue_", StringComparison.Ordinal));

        private readonly IProfitCenterRepository _repo;
        private readonly ITenantEventTypeRepository _eventTypes;
        private readonly ITenantContext _tenantContext;

        public ProfitCentersController(
            IProfitCenterRepository repo,
            ITenantEventTypeRepository eventTypes,
            ITenantContext tenantContext)
        {
            _repo = repo;
            _eventTypes = eventTypes;
            _tenantContext = tenantContext;
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;

            var centers = await _repo.ListForTenant(tenantId);
            var assignments = await _repo.ListAssignments(tenantId);
            var eventTypes = await _eventTypes.GetAllForTenant(tenantId);

            var byCenter = assignments
                .GroupBy(a => a.ProfitCenterId)
                .ToDictionary(g => g.Key, g => g.Select(a => a.RevenueKey).ToList());

            return new ApiResponses().OkResult(new ProfitCentersResponse
            {
                UsingDefaults = centers.Count == 0,
                Palette = new ProfitCenterPaletteDto
                {
                    Swatches = ProfitCenterPalette.Slots.ToList(),
                    TotalSeriesColor = ProfitCenterPalette.TotalSeries,
                },
                Centers = centers.Select(c => new ProfitCenterDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    SortOrder = c.SortOrder,
                    Color = ColorOf(c),
                    RevenueKeys = byCenter.TryGetValue(c.Id, out var keys)
                        // Stable stream order inside a bucket: the platform's own slot order.
                        ? keys.OrderBy(k => Array.IndexOf(QboAccountKeys.All, k)).ToList()
                        : new List<string>(),
                }).ToList(),
                Streams = RevenueKeys.Select(ToStreamDto).ToList(),
                EventTypes = eventTypes.Select(t => new EventRoutingDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    RevenueKey = t.RevenueKey,
                }).ToList(),
                EventRoutingOptions = EventRoutingKeys.Select(ToStreamDto).ToList(),
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertProfitCenterRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name)) return new ApiResponses().BadRequestResult("The profit center needs a name.");
            if (name.Length > 60) return new ApiResponses().BadRequestResult("Keep the name under 60 characters.");
            if (req.Color is not null && !ProfitCenterPalette.IsValid(req.Color))
                return new ApiResponses().BadRequestResult("The color must be a hex value like #eb6834.");

            var existing = await _repo.ListForTenant(_tenantContext.TenantId);
            if (existing.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                return new ApiResponses().BadRequestResult($"A profit center named \"{name}\" already exists.");

            // No color chosen: take the first palette slot this tenant isn't already using, so a
            // new center is distinguishable from the existing ones without the user thinking about it.
            var color = req.Color?.Trim() ?? ProfitCenterPalette.FirstUnused(existing.Select(c => c.Color));

            var sortOrder = existing.Count == 0 ? 0 : existing.Max(c => c.SortOrder) + 1;
            var id = await _repo.Create(_tenantContext.TenantId, name, sortOrder, color);
            return new ApiResponses().OkResult(new ProfitCenterDto
            {
                Id = id, Name = name, SortOrder = sortOrder, Color = color,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertProfitCenterRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name)) return new ApiResponses().BadRequestResult("The profit center needs a name.");
            if (name.Length > 60) return new ApiResponses().BadRequestResult("Keep the name under 60 characters.");
            if (req.Color is not null && !ProfitCenterPalette.IsValid(req.Color))
                return new ApiResponses().BadRequestResult("The color must be a hex value like #eb6834.");

            var existing = await _repo.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Profit center not found.");

            var siblings = await _repo.ListForTenant(_tenantContext.TenantId);
            if (siblings.Any(c => c.Id != id && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                return new ApiResponses().BadRequestResult($"A profit center named \"{name}\" already exists.");

            // Omitting the color keeps the current one; it never silently resets to a default.
            var color = req.Color?.Trim() ?? ColorOf(existing);
            await _repo.Update(id, _tenantContext.TenantId, name, color);
            return new ApiResponses().OkResult(new ProfitCenterDto
            {
                Id = id, Name = name, SortOrder = existing.SortOrder, Color = color,
            });
        }

        // Deleting a center never strands money: its slots' assignments cascade away and those
        // streams fall back to their built-in department in every report.
        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = await _repo.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Profit center not found.");
            await _repo.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPost("Reorder")]
        public async Task<IActionResult> Reorder([FromBody] ReorderProfitCentersRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            await _repo.UpdateSortOrders(
                _tenantContext.TenantId,
                req.Items.Select(i => i.Id).ToList(),
                req.Items.Select(i => i.SortOrder).ToList());
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPut("Assignments")]
        public async Task<IActionResult> SaveAssignments([FromBody] SaveProfitCenterAssignmentsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;

            var validKeys = RevenueKeys.ToHashSet(StringComparer.Ordinal);
            var myCenters = (await _repo.ListForTenant(tenantId)).Select(c => c.Id).ToHashSet();

            foreach (var item in req.Assignments)
            {
                if (!validKeys.Contains(item.RevenueKey))
                    return new ApiResponses().BadRequestResult($"Unknown revenue stream \"{item.RevenueKey}\".");
                if (item.ProfitCenterId is { } pcId && !myCenters.Contains(pcId))
                    return new ApiResponses().BadRequestResult("One of the assignments points at a profit center that doesn't exist.");
            }

            foreach (var item in req.Assignments)
            {
                if (item.ProfitCenterId is { } pcId)
                    await _repo.UpsertAssignment(tenantId, item.RevenueKey, pcId);
                else
                    await _repo.ClearAssignment(tenantId, item.RevenueKey);
            }
            return new ApiResponses().OkResult();
        }

        /// <summary>
        /// Creates the built-in departments as real, renamable centers, so a tenant starts from
        /// something sensible instead of an empty page. Only from a clean slate: seeding into an
        /// existing configuration would silently rewire streams the tenant already placed.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPost("SeedDefaults")]
        public async Task<IActionResult> SeedDefaults()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;

            var existing = await _repo.ListForTenant(tenantId);
            if (existing.Count > 0)
                return new ApiResponses().BadRequestResult("Profit centers already exist; add or rename them instead of re-seeding.");

            // Palette slot per department, in the same order the built-in grouping uses, so
            // seeding produces the colors the reports were already drawing before the tenant
            // opted in: seeding changes the names they can edit, never what they see.
            var slot = 0;
            for (var i = 0; i < QboDepartments.All.Length; i++)
            {
                var dept = QboDepartments.All[i];
                var memberKeys = RevenueKeys.Where(k => QboDepartments.ForRevenueKey(k) == dept).ToList();
                if (memberKeys.Count == 0) continue;
                var color = dept == QboDepartments.Other
                    ? ProfitCenterPalette.Unassigned
                    : ProfitCenterPalette.DefaultForIndex(slot++);
                var id = await _repo.Create(tenantId, QboDepartments.Label(dept), i, color);
                foreach (var key in memberKeys)
                {
                    await _repo.UpsertAssignment(tenantId, key, id);
                }
            }
            return new ApiResponses().OkResult();
        }

        /// <summary>
        /// Routes one event type's revenue to a QuickBooks slot (Script0274), which is how a
        /// clinic's tickets reach the Training Center bucket while a race's stay with the gate.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPut("EventRouting/{eventTypeId:guid}")]
        public async Task<IActionResult> SetEventRouting(Guid eventTypeId, [FromBody] SetEventRoutingRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            if (req.RevenueKey is not null && !EventRoutingKeys.Contains(req.RevenueKey, StringComparer.Ordinal))
                return new ApiResponses().BadRequestResult("Event types can only route to event ticket or Training Center revenue.");

            var existing = await _eventTypes.GetById(eventTypeId, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Event type not found.");

            await _eventTypes.SetRevenueKey(eventTypeId, _tenantContext.TenantId, req.RevenueKey);
            return new ApiResponses().OkResult();
        }

        /// <summary>A center's color, or the neutral gray for a pre-Script0276 row.</summary>
        private static string ColorOf(Services.Repositories.Data.AccountingData.ProfitCenter c) =>
            ProfitCenterPalette.IsValid(c.Color) ? c.Color!.Trim() : ProfitCenterPalette.Unassigned;

        private static RevenueStreamDto ToStreamDto(string key) => new()
        {
            Key = key,
            Label = QboAccountKeys.Label(key),
            DefaultCenterLabel = QboDepartments.Label(QboDepartments.ForRevenueKey(key)),
        };
    }
}
