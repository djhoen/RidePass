using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Multi-point inspections: a graded, component-by-component check anchored to the BIKE, so the
    // grading history accrues per machine across visits rather than being buried in one ticket.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
    public class BikeShopInspectionController : ControllerBase
    {
        private static readonly string[] ValidRatings = { "good", "monitor", "attention", "na" };

        private readonly IBikeShopRepository _shop;
        private readonly ITenantContext _tenantContext;

        public BikeShopInspectionController(IBikeShopRepository shop, ITenantContext tenantContext)
        {
            _shop = shop;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        /// <summary>The checklist this shop uses, created on first use.</summary>
        [HttpGet("Template")]
        public async Task<IActionResult> GetTemplate()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tpl = await _shop.EnsureDefaultInspectionTemplate(TenantId);
            var items = await _shop.ListTemplateItems(tpl.Id);
            return new ApiResponses().OkResult(new { template = tpl, items });
        }

        /// <summary>
        /// Starts an inspection for a bike, materialising one blank row per checklist item so the
        /// mechanic grades a list rather than building one. Next service defaults to +6 months,
        /// the industry convention.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Start([FromBody] StartInspectionRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var bike = await _shop.GetCustomerBike(req.CustomerBikeId, TenantId);
            if (bike is null) return new ApiResponses().NotFoundResult("Bike not found.");

            // A shop can keep several checklists (a quick pre-ride vs a full service, or MX vs MTB
            // if they run both). Fall back to the default when none is named.
            var tpl = req.TemplateId is Guid tid
                ? await _shop.GetInspectionTemplate(tid, TenantId)
                : await _shop.EnsureDefaultInspectionTemplate(TenantId);
            if (tpl is null) return new ApiResponses().NotFoundResult("Checklist not found.");

            var items = await _shop.ListTemplateItems(tpl.Id);
            if (items.Count == 0)
                return new ApiResponses().BadRequestResult("That checklist has no items yet. Add some under Shop Settings.");

            var insp = new ShopInspection
            {
                TenantId = TenantId,
                CustomerBikeId = req.CustomerBikeId,
                WorkOrderId = req.WorkOrderId,
                TemplateId = tpl.Id,
                PerformedByUserId = UserId,
                Status = "draft",
                NextServiceDate = req.NextServiceDate ?? DateTime.UtcNow.Date.AddMonths(6),
            };

            // Labels are snapshotted here: editing the checklist later must not rewrite what a past
            // inspection recorded.
            var results = items.Select(i => new ShopInspectionResult
            {
                TemplateItemId = i.Id,
                GroupLabel = i.GroupLabel,
                Label = i.Label,
                Rating = "na",
                SortOrder = i.SortOrder,
            });

            var id = await _shop.CreateInspection(insp, results);
            var created = await _shop.GetInspection(id, TenantId);
            return new ApiResponses().OkResult(created);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var insp = await _shop.GetInspection(id, TenantId);
            return insp is null
                ? new ApiResponses().NotFoundResult("Inspection not found.")
                : new ApiResponses().OkResult(insp);
        }

        /// <summary>Every inspection on a bike, newest first.</summary>
        [HttpGet("ForBike/{bikeId:guid}")]
        public async Task<IActionResult> ForBike(Guid bikeId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var bike = await _shop.GetCustomerBike(bikeId, TenantId);
            if (bike is null) return new ApiResponses().NotFoundResult("Bike not found.");
            return new ApiResponses().OkResult(await _shop.ListInspectionsForBike(bikeId, TenantId));
        }

        /// <summary>Saves grades, notes, next-service date, and optionally marks it complete.</summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Save(Guid id, [FromBody] SaveInspectionRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var insp = await _shop.GetInspection(id, TenantId);
            if (insp is null) return new ApiResponses().NotFoundResult("Inspection not found.");

            if (req.Status is not ("draft" or "complete"))
                return new ApiResponses().BadRequestResult("Status must be draft or complete.");

            var rows = req.Results ?? new List<SaveInspectionResultRow>();
            foreach (var r in rows)
            {
                if (!ValidRatings.Contains(r.Rating))
                    return new ApiResponses().BadRequestResult($"'{r.Rating}' isn't a valid rating.");
            }
            // Only rows that belong to THIS inspection are writable, so a crafted request can't
            // reach another inspection's results.
            var own = insp.Results.Select(r => r.Id).ToHashSet();
            var writable = rows.Where(r => own.Contains(r.Id))
                               .Select(r => (r.Id, r.Rating, r.Notes));

            await _shop.SaveInspectionResults(id, TenantId, writable);
            await _shop.UpdateInspectionHeader(id, TenantId, req.Status, req.NextServiceDate, req.SummaryNotes);

            return new ApiResponses().OkResult(await _shop.GetInspection(id, TenantId));
        }
    
        // ── Checklist templates ───────────────────────────────────────────────
        // The checklist is data, not code: an MX track checks fork seals and air filters, a bike
        // park checks spoke tension and bar tape, and every shop wants its own wording.
        [HttpGet("Templates")]
        public async Task<IActionResult> ListTemplates()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            // Guarantees a tenant always has at least one checklist to edit.
            await _shop.EnsureDefaultInspectionTemplate(TenantId);
            var templates = await _shop.ListInspectionTemplates(TenantId);
            var result = new List<object>();
            foreach (var t in templates)
            {
                result.Add(new
                {
                    t.Id, t.Name, t.IsDefault, t.IsActive, t.SortOrder,
                    items = await _shop.ListTemplateItems(t.Id),
                });
            }
            return new ApiResponses().OkResult(result);
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Templates")]
        public async Task<IActionResult> CreateTemplate([FromBody] UpsertInspectionTemplateRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(req.Name))
                return new ApiResponses().BadRequestResult("Give the checklist a name.");
            var id = await _shop.CreateInspectionTemplate(TenantId, req.Name.Trim());
            return new ApiResponses().OkResult(new { id });
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Templates/{id:guid}")]
        public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] UpsertInspectionTemplateRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(req.Name))
                return new ApiResponses().BadRequestResult("Give the checklist a name.");
            var existing = await _shop.GetInspectionTemplate(id, TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Checklist not found.");
            await _shop.UpdateInspectionTemplate(id, TenantId, req.Name.Trim(), req.IsActive);
            if (req.MakeDefault) await _shop.SetDefaultInspectionTemplate(id, TenantId);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Templates/{templateId:guid}/Items")]
        public async Task<IActionResult> UpsertItem(Guid templateId, [FromBody] UpsertInspectionItemRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(req.GroupLabel) || string.IsNullOrWhiteSpace(req.Label))
                return new ApiResponses().BadRequestResult("A checklist row needs a group and a label.");
            var tpl = await _shop.GetInspectionTemplate(templateId, TenantId);
            if (tpl is null) return new ApiResponses().NotFoundResult("Checklist not found.");

            var id = await _shop.UpsertTemplateItem(new ShopInspectionTemplateItem
            {
                Id = req.Id ?? Guid.Empty,
                TemplateId = templateId,
                GroupLabel = req.GroupLabel.Trim(),
                Label = req.Label.Trim(),
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
            }, TenantId);
            return new ApiResponses().OkResult(new { id });
        }

        /// <summary>
        /// Removes a checklist row. Past inspections keep their recorded label and rating — results
        /// snapshot both — so this only changes what future inspections ask about.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Templates/Items/{itemId:guid}")]
        public async Task<IActionResult> DeleteItem(Guid itemId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var removed = await _shop.DeleteTemplateItem(itemId, TenantId);
            return removed > 0
                ? new ApiResponses().OkResult()
                : new ApiResponses().NotFoundResult("Checklist row not found.");
        }
}
}
