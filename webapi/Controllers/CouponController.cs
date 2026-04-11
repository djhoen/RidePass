using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using webapi.Models;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Repositories.Data.CouponData;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CouponController : ControllerBase
    {
        private readonly ICouponRepository _couponRepository;

        public CouponController(ICouponRepository couponRepository)
        {
            _couponRepository = couponRepository;
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("GetCoupons")]
        public async Task<IActionResult> GetCoupons()
        {
            try
            {
                var coupons = await _couponRepository.GetCoupons();

                return new ApiResponses().OkResult(coupons);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpGet("GetCoupon")]
        public async Task<IActionResult> GetCoupon([FromQuery] string id)
        {
            try
            {
                var coupon = await _couponRepository.GetCoupon(id);

                return new ApiResponses().OkResult(coupon);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("CreateCoupon")]
        public async Task<IActionResult> CreateCoupon([FromBody] CouponRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var coupon = new Coupon
                {
                    Code = request.Code,
                    Amount = request.Amount,
                    CouponTypeId = request.CouponTypeId,
                    ProductId = request.ProductId,
                    Description = request.Description,
                    StartDate = request.StartDate,
                    ExpireDate = request.ExpireDate,
                    UserUsageLimit = request.UserUsageLimit,
                    TotalUsageLimit = request.TotalUsageLimit,
                    ApplyToMultipleOrderItems = request.ApplyToMultipleOrderItems
                };

                var result = await _couponRepository.CreateCoupon(coupon);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("UpdateCoupon")]
        public async Task<IActionResult> UpdateCoupon([FromBody] CouponRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var coupon = new Coupon
                {
                    Id = request.Id ?? 0,
                    Code = request.Code,
                    Amount = request.Amount,
                    CouponTypeId = request.CouponTypeId,
                    ProductId = request.ProductId,
                    Description = request.Description,
                    StartDate = request.StartDate,
                    ExpireDate = request.ExpireDate,
                    UserUsageLimit = request.UserUsageLimit,
                    TotalUsageLimit = request.TotalUsageLimit,
                    ApplyToMultipleOrderItems = request.ApplyToMultipleOrderItems
                };

                await _couponRepository.UpdateCoupon(coupon);

                return new ApiResponses().OkResult(coupon);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }
    }
}
