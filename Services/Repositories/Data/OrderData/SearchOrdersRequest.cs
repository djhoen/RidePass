namespace Services.Repositories.Data.OrderData
{
    public class SearchOrdersRequest
    {
        public int? OrderId { get; set; }
        public string? Email { get; set; }
        public string? UserId { get; set; }
        public string? CouponCode { get; set; }
        public List<int>? StatusIds { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? PaymentStatus { get; set; }
        public string? StripePaymentId { get; set; }
        public List<int>? OrderSourceIds { get; set; }
    }
}
