using Services.Repositories.Data.CartData;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Data.UserData;

namespace Services.Helpers.Interfaces
{
    public interface ICouponHelper
    {
        Task<ApplyCouponResponse> ApplyCoupon(Cart cart, Coupon coupon);
        Task<GetCouponAvailabilityResponse> GetCouponWithAvailability(string couponCode, Cart cart, User? user);
    }
}
