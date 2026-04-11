namespace Services.Repositories.Data.OrderData
{
    public class Order
    {
        public int Id { get; set; }
        public DateTime OrderDate { get; set; }
        public string UserId { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal SubTotal { get; set; }
        public decimal Tax { get; set; }
        public int OrderStatusId { get; set; }
        public string? PaymentStatus { get; set; }
        public string? StripePaymentId { get; set; }
        public string? StripeSessionId { get; set; }
        public string? CouponCode { get; set; }
        public decimal CouponDiscount { get; set; }
        public int? BillingAddressId { get; set; }
        public int? ShippingAddressId { get; set; }
        public int? OrderSourceId { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}
