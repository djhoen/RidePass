using Services.Repositories.Data.AddressData;
using Services.Repositories.Data.CartData;
using Services.Repositories.Data.UserData;

namespace Services.Repositories.Data.OrderData
{
    public class CreateOrderRequest
    {
        public Cart Cart { get; set; }
        public Address? BillingAddress { get; set; }
        public Address? ShippingAddress { get; set; }
        public User? User { get; set; }
        public string? StripePaymentId { get; set; }
        public string? StripeSessionId { get; set; }
        public string? PaymentStatus { get; set; }
        public int OrderStatusId { get; set; }
        public int? OrderSourceId { get; set; }
    }

    public class CreateOrderResult
    {
        public Order Order { get; set; }
        public List<OrderItem> OrderItems { get; set; }
        public OrderCoupon? OrderCoupon { get; set; }
    }
}
