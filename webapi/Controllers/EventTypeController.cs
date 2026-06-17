using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.EventType;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventTypeController : ControllerBase
    {
        private readonly ITenantEventTypeRepository _repo;
        private readonly ITenantContext _tenantContext;
        private readonly IImageStorage _imageStorage;

        public EventTypeController(ITenantEventTypeRepository repo, ITenantContext tenantContext, IImageStorage imageStorage)
        {
            _repo = repo;
            _tenantContext = tenantContext;
            _imageStorage = imageStorage;
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
                ImageUrl = request.ImageUrl,
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

            await _repo.Update(id, _tenantContext.TenantId, request.Name, request.Color, request.ImageUrl, request.SortOrder);
            existing.Name = request.Name;
            existing.Color = request.Color;
            existing.ImageUrl = request.ImageUrl;
            existing.SortOrder = request.SortOrder;
            return new ApiResponses().OkResult(ToResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Reorder")]
        public async Task<IActionResult> Reorder([FromBody] ReorderEventTypesRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            var ids = req.Items.Select(i => i.Id).ToList();
            var orders = req.Items.Select(i => i.SortOrder).ToList();
            await _repo.UpdateSortOrders(_tenantContext.TenantId, ids, orders);
            return new ApiResponses().OkResult();
        }

        // Tenant toggle: accept Loam Pass credits for entry to this event type. Practice is
        // forced on and can't be turned off. Only meaningful when the tenant is a LoamPassMx track.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}/LoampassRedemption")]
        public async Task<IActionResult> SetLoampassRedemption(Guid id, [FromBody] SetEventTypeLoampassRequest request)
        {
            var existing = await _repo.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Event type not found.");
            }

            var allow = existing.Code == "practice" || request.Allow;   // practice can't be disabled
            await _repo.SetLoampassRedemption(id, _tenantContext.TenantId, allow);
            existing.AllowLoampassRedemption = allow;
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

        /// <summary>
        /// Uploads an image and returns its public URL. The frontend then patches the
        /// event type via the regular Update endpoint with that URL. Decoupled from row
        /// mutation so an upload can stage before save and be discarded.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Image")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                return new ApiResponses().BadRequestResult("File is required.");
            if (file.Length > 5 * 1024 * 1024)
                return new ApiResponses().BadRequestResult("File exceeds 5 MB limit.");
            var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
            };
            if (!allowed.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult($"Unsupported content type: {file.ContentType}.");

            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "eventtype", ext, ct);
            return new ApiResponses().OkResult(new { imageUrl = url });
        }

        private static EventTypeResponse ToResponse(TenantEventType row) => new()
        {
            Id = row.Id,
            Code = row.Code,
            Name = row.Name,
            Color = row.Color,
            ImageUrl = row.ImageUrl,
            SortOrder = row.SortOrder,
            IsSystem = row.IsSystem,
            // Practice always accepts Loam Pass credits, regardless of the stored flag.
            AllowLoampassRedemption = row.Code == "practice" || row.AllowLoampassRedemption,
        };
    }
}
