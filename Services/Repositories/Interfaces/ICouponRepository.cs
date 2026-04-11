using Services.Repositories.Data.CouponData;

namespace Services.Repositories.Interfaces
{
    public interface ICouponRepository
    {
        Task<List<Coupon>> GetCoupons();
        Task<int> CreateCoupon(Coupon coupon);
        Task<Coupon> GetCoupon(string couponCode);
        Task<Coupon> GetCoupon(int couponId);
        Task<int> GetCouponTotalUsageCount(string couponCode);
        Task<int> GetCouponUserUsageCount(string couponCode, string userId);
        Task UpdateCoupon(Coupon coupon);
    }
}
