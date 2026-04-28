using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.DayPass;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DayPassProductController : ControllerBase
    {
        private readonly IDayPassProductRepository _repo;
        private readonly ITenantContext _tenantContext;

        public DayPassProductController(IDayPassProductRepository repo, ITenantContext tenantContext)
        {
            _repo = repo;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetActive()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved for this request.");
            }

            var rows = await _repo.GetAllForTenant(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(rows.Select(ToResponse));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Admin")]
        public async Task<IActionResult> GetAllForAdmin()
        {
            var rows = await _repo.GetAllForTenant(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(rows.Select(ToResponse));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertDayPassProductRequest request)
        {
            var product = new DayPassProduct
            {
                TenantId = _tenantContext.TenantId,
                Name = request.Name,
                Description = request.Description,
                PriceCents = request.PriceCents,
                IsActive = request.IsActive,
                SortOrder = request.SortOrder,
            };
            product.Id = await _repo.Create(product);
            return new ApiResponses().OkResult(ToResponse(product));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertDayPassProductRequest request)
        {
            var existing = await _repo.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Product not found.");
            }

            existing.Name = request.Name;
            existing.Description = request.Description;
            existing.PriceCents = request.PriceCents;
            existing.IsActive = request.IsActive;
            existing.SortOrder = request.SortOrder;

            await _repo.Update(existing);
            return new ApiResponses().OkResult(ToResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _repo.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Product not found.");
            }
            await _repo.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        private static DayPassProductResponse ToResponse(DayPassProduct p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            PriceCents = p.PriceCents,
            IsActive = p.IsActive,
            SortOrder = p.SortOrder,
        };
    }
}
