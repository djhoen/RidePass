using Services.Helpers.Interfaces;
using Services.Repositories.Data.CartData;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace Services.Helpers
{
    public class CouponHelper : ICouponHelper
    {
        private readonly ICouponRepository _couponRepository;

        public CouponHelper(ICouponRepository couponRepository)
        {
            _couponRepository = couponRepository;
        }

        public async Task<ApplyCouponResponse> ApplyCoupon(Cart cart, Coupon coupon)
        {
            var response = new ApplyCouponResponse
            {
                Cart = cart,
                Coupon = coupon,
                CouponApplied = false
            };

            if (coupon == null || cart == null || cart.Items == null || !cart.Items.Any())
            {
                return response;
            }

            decimal discount = 0;

            if (coupon.CouponTypeId == (int)CouponType.PercentOff)
            {
                discount = cart.SubTotal * (coupon.Amount / 100);
            }
            else if (coupon.CouponTypeId == (int)CouponType.AmountOff)
            {
                discount = coupon.Amount;
            }

            if (discount > cart.SubTotal)
            {
                discount = cart.SubTotal;
            }

            cart.CouponDiscount = discount;
            cart.Total = cart.SubTotal - cart.CouponDiscount + cart.TaxesAndFees;

            response.Cart = cart;
            response.CouponApplied = discount > 0;
            response.OrderCoupon = new Repositories.Data.OrderData.OrderCoupon
            {
                CouponCode = coupon.Code,
                CouponAmount = discount,
                CouponDescription = coupon.Description
            };

            return response;
        }

        public async Task<GetCouponAvailabilityResponse> GetCouponWithAvailability(string couponCode, Cart cart, User? user)
        {
            var response = new GetCouponAvailabilityResponse();

            var coupon = await _couponRepository.GetCoupon(couponCode);
            if (coupon == null)
            {
                response.Message = "Coupon not found";
                return response;
            }

            // Check expiration
            if (coupon.ExpireDate.HasValue && coupon.ExpireDate.Value < DateTime.UtcNow)
            {
                response.Message = "Coupon has expired";
                response.Coupon = coupon;
                return response;
            }

            // Check start date
            if (coupon.StartDate.HasValue && coupon.StartDate.Value > DateTime.UtcNow)
            {
                response.Message = "Coupon is not yet active";
                response.Coupon = coupon;
                return response;
            }

            // Check total usage limit
            if (coupon.TotalUsageLimit.HasValue)
            {
                var totalUsage = await _couponRepository.GetCouponTotalUsageCount(couponCode);
                if (totalUsage >= coupon.TotalUsageLimit.Value)
                {
                    response.Message = "Coupon usage limit has been reached";
                    response.Coupon = coupon;
                    return response;
                }
            }

            // Check per-user usage limit
            if (coupon.UserUsageLimit.HasValue && user != null)
            {
                var userUsage = await _couponRepository.GetCouponUserUsageCount(couponCode, user.Id);
                if (userUsage >= coupon.UserUsageLimit.Value)
                {
                    response.Message = "You have already used this coupon";
                    response.Coupon = coupon;
                    return response;
                }
            }

            coupon.AvailableToUse = true;
            response.Coupon = coupon;
            response.Message = "Coupon is available";

            return response;
        }
    }
}
