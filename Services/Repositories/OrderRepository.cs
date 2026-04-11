using Dapper;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.OrderData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly IDbHelper _dbHelper;
        public OrderRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
        }

        public async Task<int> CreateOrder(Order order)
        {
            var sql = @"INSERT INTO ""order"" (""orderDate"", ""userId"", ""totalAmount"", ""orderStatusId"", ""subTotal"", ""tax"", ""billingAddressId"", ""shippingAddressId"", ""email"", ""stripePaymentId"", ""paymentStatus"", ""stripeSessionId"", ""couponCode"", ""couponDiscount"", ""orderSourceId"")
                        VALUES (@orderDate, @userId, @totalAmount, @orderStatusId, @subTotal, @tax, @billingAddressId, @shippingAddressId, @email, @stripePaymentId, @paymentStatus, @stripeSessionId, @couponCode, @couponDiscount, @orderSourceId)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

            var result = await _dbHelper.Query<int>(sql, order);
            return result.FirstOrDefault();
        }

        public async Task<int> CreateOrderItem(OrderItem orderItem)
        {
            var sql = @"INSERT INTO ""order.item"" (""orderId"", ""productId"", ""price"", ""qty"", ""parentOrderItemId"")
                        VALUES (@orderId, @productId, @price, @qty, @parentOrderItemId)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

            var result = await _dbHelper.Query<int>(sql, orderItem);

            return result.FirstOrDefault();
        }

        public async Task<int> CreateOrderCoupon(OrderCoupon orderCoupon)
        {
            var sql = @"INSERT INTO ""order.coupon"" (""orderId"", ""couponCode"", ""couponAmount"", ""couponDescription"")
                        VALUES (@orderId, @couponCode, @couponAmount, @couponDescription)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

            var result = await _dbHelper.Query<int>(sql, orderCoupon);

            return result.FirstOrDefault();
        }

        public async Task<int> CreateOrderNote(OrderNote orderNote)
        {
            var sql = @"INSERT INTO ""order.note"" (""orderId"", ""note"", ""createdByUserId"", ""createdDate"")
                        VALUES (@orderId, @note, @createdByUserId, @createdDate)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

            var result = await _dbHelper.Query<int>(sql, orderNote);

            return result.FirstOrDefault();
        }

        public async Task DeleteOrderCoupons(int orderId)
        {
            var sql = @"DELETE FROM ""order.coupon"" WHERE ""orderId"" = @orderId";

            await _dbHelper.Execute(sql, new { orderId });
        }

        public async Task DeleteOrderItems(int orderId)
        {
            var sql = @"DELETE FROM ""order.item"" WHERE ""orderId"" = @orderId";

            await _dbHelper.Execute(sql, new { orderId });
        }

        public async Task<Order> GetOrder(int orderId)
        {
            var sql = @"SELECT * FROM ""order"" WHERE ""id"" = @orderId";

            var order = await _dbHelper.Query<Order>(sql, new { orderId });

            return order.FirstOrDefault();
        }

        public async Task<Order> GetOrderByStripePaymentId(string stripePaymentId)
        {
            var sql = @"SELECT * FROM ""order"" WHERE ""stripePaymentId"" = @stripePaymentId";

            var order = await _dbHelper.Query<Order>(sql, new { stripePaymentId });

            return order.FirstOrDefault();
        }

        public async Task<Order> GetOrderByStripeSessionId(string stripeSessionId)
        {
            var sql = @"SELECT * FROM ""order"" WHERE ""stripeSessionId"" = @stripeSessionId";

            var order = await _dbHelper.Query<Order>(sql, new { stripeSessionId });

            return order.FirstOrDefault();
        }

        public async Task<OrderCoupon> GetOrderCoupon(int orderId)
        {
            var sql = @"SELECT * FROM ""order.coupon"" WHERE ""orderId"" = @orderId";

            var result = await _dbHelper.Query<OrderCoupon>(sql, new { orderId });

            return result.FirstOrDefault();
        }

        public async Task<OrderItem> GetOrderItem(int orderItemId)
        {
            var sql = @"SELECT * FROM ""order.item"" WHERE ""id"" = @orderItemId";

            var orderItems = await _dbHelper.Query<OrderItem>(sql, new { orderItemId });

            return orderItems.FirstOrDefault();
        }

        public async Task<List<OrderItem>> GetOrderItems(int orderId)
        {
            var sql = $@"SELECT oi.*, p.""name"" AS ProductName
                        FROM ""order.item"" oi
                            JOIN ""product"" p ON p.""id"" = oi.""productId""
                        WHERE oi.""orderId"" = @orderId";

            var orderItems = await _dbHelper.Query<OrderItem>(sql, new { orderId });

            return orderItems.ToList();
        }

        public async Task<List<OrderNote>> GetOrderNotes(int orderId)
        {
            var sql = $@"SELECT *
                        FROM ""order.note""
                        WHERE ""orderId"" = @orderId";

            var orderNotes = await _dbHelper.Query<OrderNote>(sql, new { orderId });

            return orderNotes.ToList();
        }

        public async Task<List<Order>> GetUserOrders(string userId)
        {
            var sql = @"SELECT *
                        FROM ""order""
                        WHERE ""userId"" = @userId
                        ORDER BY ""id"" DESC";

            var orders = await _dbHelper.Query<Order>(sql, new { userId });

            return orders != null ? orders.ToList() : new List<Order>();
        }

        public async Task<List<Order>> SearchOrders(SearchOrdersRequest request)
        {
            var whereClause = "WHERE 1 = 1 ";
            var args = new DynamicParameters();

            if (request.OrderId > 0)
            {
                args.Add("@orderId", request.OrderId);
                whereClause += @"AND o.""id"" = @orderId ";
            }
            else if (!string.IsNullOrEmpty(request.StripePaymentId))
            {
                args.Add("@stripePaymentId", request.StripePaymentId);
                whereClause += @"AND o.""stripePaymentId"" = @stripePaymentId ";
            }
            else
            {
                if (!string.IsNullOrEmpty(request.Email))
                {
                    args.Add("@email", request.Email);
                    whereClause += @"AND o.""email"" = @email ";
                }

                if (!string.IsNullOrEmpty(request.UserId))
                {
                    args.Add("@userId", request.UserId);
                    whereClause += @"AND o.""userId"" = @userId ";
                }

                if (!string.IsNullOrEmpty(request.CouponCode))
                {
                    args.Add("@couponCode", request.CouponCode);
                    whereClause += @"AND o.""couponCode"" = @couponCode ";
                }

                if (request.StatusIds != null && request.StatusIds.Count > 0)
                {
                    args.Add("@statusIds", request.StatusIds);
                    whereClause += @"AND o.""orderStatusId"" = ANY (@statusIds) ";
                }

                if (request.StartDate.HasValue)
                {
                    args.Add("@startDate", request.StartDate.Value);
                    whereClause += @"AND o.""orderDate"" >= @startDate ";
                }

                if (request.EndDate.HasValue)
                {
                    args.Add("@endDate", request.EndDate.Value);
                    whereClause += @"AND o.""orderDate"" <= @endDate ";
                }

                if (request.OrderSourceIds != null && request.OrderSourceIds.Count > 0)
                {
                    args.Add("@orderSourceIds", request.OrderSourceIds);
                    whereClause += @"AND o.""orderSourceId"" = ANY(@orderSourceIds) ";
                }

                if (!string.IsNullOrEmpty(request.PaymentStatus))
                {
                    args.Add("@paymentStatus", request.PaymentStatus);
                    whereClause += @"AND o.""paymentStatus"" = @paymentStatus ";
                }
            }

            var sql = $@"SELECT o.*, u.""firstName"", u.""lastName""
                        FROM ""order"" o
                            LEFT JOIN ""user"" u ON u.""id"" = o.""userId""
                        {whereClause}
                        ORDER BY o.""orderDate"" DESC";

            var orders = await _dbHelper.Query<Order>(sql, args);

            return orders != null ? orders.ToList() : new List<Order>();
        }

        public async Task UpdateOrder(Order order)
        {
            var sql = @"UPDATE ""order""
                        SET ""orderStatusId"" = @orderStatusId,
                            ""subTotal"" = @subTotal,
                            ""totalAmount"" = @totalAmount,
                            ""paymentStatus"" = @paymentStatus,
                            ""tax"" = @tax,
                            ""billingAddressId"" = @billingAddressId,
                            ""shippingAddressId"" = @shippingAddressId,
                            ""stripePaymentId"" = @stripePaymentId,
                            ""couponCode"" = @couponCode,
                            ""couponDiscount"" = @couponDiscount
                        WHERE ""id"" = @id";
            await _dbHelper.Execute(sql, order);
        }
    }
}
