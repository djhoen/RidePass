namespace Services.Repositories.Data.OrderData
{
    public class OrderCoupon
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string CouponCode { get; set; }
        public decimal CouponAmount { get; set; }
        public string? CouponDescription { get; set; }
    }
}
