using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Pass;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PassProductController : ControllerBase
    {
        private readonly IPassProductRepository _repo;
        private readonly ITenantContext _tenantContext;

        public PassProductController(IPassProductRepository repo, ITenantContext tenantContext)
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
        public async Task<IActionResult> Create([FromBody] UpsertPassProductRequest request)
        {
            var product = new PassProduct
            {
                TenantId = _tenantContext.TenantId,
                Name = request.Name,
                Description = request.Description,
                PriceCents = request.PriceCents,
                IsActive = request.IsActive,
                SortOrder = request.SortOrder,
                RequiresWaiver = request.RequiresWaiver,
                RiderPaidServiceChargeBps = request.RiderPaidServiceChargeBps,
            };
            product.Id = await _repo.Create(product);
            return new ApiResponses().OkResult(ToResponse(product));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertPassProductRequest request)
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
            existing.RequiresWaiver = request.RequiresWaiver;
            existing.RiderPaidServiceChargeBps = request.RiderPaidServiceChargeBps;

            await _repo.Update(existing);
            return new ApiResponses().OkResult(ToResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Reorder")]
        public async Task<IActionResult> Reorder([FromBody] ReorderPassProductsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            var ids = req.Items.Select(i => i.Id).ToList();
            var orders = req.Items.Select(i => i.SortOrder).ToList();
            await _repo.UpdateSortOrders(_tenantContext.TenantId, ids, orders);
            return new ApiResponses().OkResult();
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

        private static PassProductResponse ToResponse(PassProduct p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            PriceCents = p.PriceCents,
            IsActive = p.IsActive,
            SortOrder = p.SortOrder,
            RequiresWaiver = p.RequiresWaiver,
            RiderPaidServiceChargeBps = p.RiderPaidServiceChargeBps,
        };
    }
}
