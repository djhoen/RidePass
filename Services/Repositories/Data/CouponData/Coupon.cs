namespace Services.Repositories.Data.CouponData
{
    public class Coupon
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public decimal Amount { get; set; }
        public int CouponTypeId { get; set; }
        public string? CouponType { get; set; }
        public int? ProductId { get; set; }
        public string? Product { get; set; }
        public string? Description { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? ExpireDate { get; set; }
        public int? UserUsageLimit { get; set; }
        public int? TotalUsageLimit { get; set; }
        public bool ApplyToMultipleOrderItems { get; set; }
        public bool AvailableToUse { get; set; }
    }

    public enum CouponType
    {
        PercentOff = 1,
        AmountOff = 2
    }

    public enum CouponUsageLimitType
    {
        PerUser = 1,
        TotalUsed = 2
    }

    public class ApplyCouponResponse
    {
        public Coupon? Coupon { get; set; }
        public Services.Repositories.Data.OrderData.OrderCoupon? OrderCoupon { get; set; }
        public Services.Repositories.Data.CartData.Cart? Cart { get; set; }
        public bool CouponApplied { get; set; }
    }

    public class GetCouponAvailabilityResponse
    {
        public Coupon? Coupon { get; set; }
        public string? Message { get; set; }
    }
}
