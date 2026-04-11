using Services.Repositories.Data.OrderData;

namespace Services.Repositories.Interfaces
{
    public interface IOrderRepository
    {
        Task<int> CreateOrder(Order order);
        Task<int> CreateOrderItem(OrderItem orderItem);
        Task<int> CreateOrderCoupon(OrderCoupon orderCoupon);
        Task<int> CreateOrderNote(OrderNote orderNote);
        Task DeleteOrderCoupons(int orderId);
        Task DeleteOrderItems(int orderId);
        Task<Order> GetOrder(int orderId);
        Task<Order> GetOrderByStripePaymentId(string stripePaymentId);
        Task<Order> GetOrderByStripeSessionId(string stripeSessionId);
        Task<OrderCoupon> GetOrderCoupon(int orderId);
        Task<OrderItem> GetOrderItem(int orderItemId);
        Task<List<OrderItem>> GetOrderItems(int orderId);
        Task<List<OrderNote>> GetOrderNotes(int orderId);
        Task<List<Order>> GetUserOrders(string userId);
        Task<List<Order>> SearchOrders(SearchOrdersRequest req);
        Task UpdateOrder(Order order);
    }
}
