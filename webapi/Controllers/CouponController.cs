using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Coupon;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.CampaignsManage)]
    public class CouponController : ControllerBase
    {
        private readonly ICouponRepository _coupons;
        private readonly ITenantContext _tenantContext;

        public CouponController(ICouponRepository coupons, ITenantContext tenantContext)
        {
            _coupons = coupons;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _coupons.ListByTenant(_tenantContext.TenantId);
            var responses = new List<CouponResponse>(rows.Count);
            foreach (var c in rows)
            {
                responses.Add(await ToResponse(c));
            }
            return new ApiResponses().OkResult(responses);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertCouponRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!ValidateDiscountValue(request, out var msg)) return new ApiResponses().BadRequestResult(msg);

            // Reject duplicate codes (case-insensitive) up front so the unique-index error
            // doesn't bubble up as a generic 500.
            var existing = await _coupons.GetByCode(_tenantContext.TenantId, request.Code);
            if (existing is not null)
                return new ApiResponses().BadRequestResult($"Coupon code '{request.Code}' is already in use.");

            Guid? createdBy = null;
            if (Guid.TryParse(User.FindFirst("UserId")?.Value, out var u)) createdBy = u;

            var coupon = new Coupon
            {
                TenantId = _tenantContext.TenantId,
                Code = request.Code,
                Description = request.Description,
                DiscountKind = request.DiscountKind,
                DiscountValue = request.DiscountValue,
                ApplicableScope = request.ApplicableScope,
                ApplicableEventId = request.ApplicableEventId,
                ValidFromUtc = request.ValidFromUtc,
                ValidToUtc = request.ValidToUtc,
                MaxTotalUses = request.MaxTotalUses,
                MaxUsesPerUser = request.MaxUsesPerUser,
                IsActive = request.IsActive,
                CreatedByUserId = createdBy,
            };
            coupon.Id = await _coupons.Create(coupon);
            return new ApiResponses().OkResult(await ToResponse(coupon));
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertCouponRequest request)
        {
            var existing = await _coupons.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Coupon not found.");
            if (!ValidateDiscountValue(request, out var msg)) return new ApiResponses().BadRequestResult(msg);

            if (!string.Equals(existing.Code, request.Code, StringComparison.OrdinalIgnoreCase))
            {
                var dup = await _coupons.GetByCode(_tenantContext.TenantId, request.Code);
                if (dup is not null && dup.Id != id)
                    return new ApiResponses().BadRequestResult($"Coupon code '{request.Code}' is already in use.");
            }

            existing.Code = request.Code;
            existing.Description = request.Description;
            existing.DiscountKind = request.DiscountKind;
            existing.DiscountValue = request.DiscountValue;
            existing.ApplicableScope = request.ApplicableScope;
            existing.ApplicableEventId = request.ApplicableEventId;
            existing.ValidFromUtc = request.ValidFromUtc;
            existing.ValidToUtc = request.ValidToUtc;
            existing.MaxTotalUses = request.MaxTotalUses;
            existing.MaxUsesPerUser = request.MaxUsesPerUser;
            existing.IsActive = request.IsActive;
            await _coupons.Update(existing);
            return new ApiResponses().OkResult(await ToResponse(existing));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _coupons.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Coupon not found.");
            // We allow deletion even if it has redemptions — the FK cascades. If you want
            // to preserve redemption history, switch to a soft delete (set is_active=false)
            // by removing this method or using deactivate instead.
            await _coupons.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        private async Task<CouponResponse> ToResponse(Coupon c)
        {
            var redemptions = await _coupons.CountRedemptions(c.Id);
            return new CouponResponse
            {
                Id = c.Id,
                Code = c.Code,
                Description = c.Description,
                DiscountKind = c.DiscountKind,
                DiscountValue = c.DiscountValue,
                ApplicableScope = c.ApplicableScope,
                ApplicableEventId = c.ApplicableEventId,
                ValidFromUtc = c.ValidFromUtc,
                ValidToUtc = c.ValidToUtc,
                MaxTotalUses = c.MaxTotalUses,
                MaxUsesPerUser = c.MaxUsesPerUser,
                IsActive = c.IsActive,
                RedemptionCount = redemptions,
                CreatedAt = c.CreatedAt,
            };
        }

        private static bool ValidateDiscountValue(UpsertCouponRequest r, out string err)
        {
            if (r.DiscountKind == "percent" && r.DiscountValue > 10000)
            {
                err = "Percent discount can't exceed 10000 bps (100%).";
                return false;
            }
            err = string.Empty;
            return true;
        }
    }
}
