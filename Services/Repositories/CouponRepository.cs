using Services.Helpers.Interfaces;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Data.OrderData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class CouponRepository : ICouponRepository
    {
        private readonly IDbHelper _dbHelper;
        public CouponRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
        }

        public async Task<int> CreateCoupon(Coupon coupon)
        {
            var existingCoupon = await GetCoupon(coupon.Code);

            if (existingCoupon == null)
            {
                var sql = @"INSERT INTO ""coupon"" (""code"", ""startDate"", ""expireDate"", ""productId"", ""couponTypeId"", ""amount"", ""userUsageLimit"", ""description"", ""totalUsageLimit"", ""applyToMultipleOrderItems"")
                        VALUES (@code, @startDate, @expireDate, @productId, @couponTypeId, @amount, @userUsageLimit, @description, @totalUsageLimit, @applyToMultipleOrderItems)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

                var id = await _dbHelper.Query<int>(sql, coupon);

                return id.FirstOrDefault();
            }
            else
            {
                throw new Exception($"Error: Coupon with the name {coupon.Code} already exists");
            }
        }

        public async Task<List<Coupon>> GetCoupons()
        {
            var sql = @"SELECT c.*, p.""name"" AS Product, ct.""name"" as CouponType
                        FROM ""coupon"" c
                            LEFT JOIN ""product"" p ON p.""id"" = c.""productId""
                            JOIN ""coupon.type"" ct ON ct.""id"" = c.""couponTypeId""";
            var result = await _dbHelper.Query<Coupon>(sql);
            return result.ToList();
        }

        public async Task<Coupon> GetCoupon(string couponCode)
        {
            var sql = @"SELECT c.*, p.""name"" AS Product, ct.""name"" as CouponType
                        FROM ""coupon"" c
                            LEFT JOIN ""product"" p ON p.""id"" = c.""productId""
                            JOIN ""coupon.type"" ct ON ct.""id"" = c.""couponTypeId""
                        WHERE ""code"" = @couponCode";
            var result = await _dbHelper.Query<Coupon>(sql, new { couponCode });
            return result.FirstOrDefault();
        }

        public async Task<Coupon> GetCoupon(int couponId)
        {
            var sql = @"SELECT c.*, p.""name"" AS Product, ct.""name"" as CouponType
                        FROM ""coupon"" c
                            LEFT JOIN ""product"" p ON p.""id"" = c.""productId""
                            JOIN ""coupon.type"" ct ON ct.""id"" = c.""couponTypeId""
                        WHERE c.""id"" = @couponId";
            var result = await _dbHelper.Query<Coupon>(sql, new { couponId });
            return result.FirstOrDefault();
        }

        public async Task<int> GetCouponTotalUsageCount(string couponCode)
        {
            var completeOrderStatusId = (int)OrderStatus.Complete;
            var sql = @"SELECT COUNT (*)
                        FROM ""order""
                        WHERE ""couponCode"" = @couponCode
                            AND ""orderStatusId"" = @completeOrderStatusId";
            var result = await _dbHelper.Query<int>(sql, new { couponCode, completeOrderStatusId });
            return result.FirstOrDefault();
        }

        public async Task<int> GetCouponUserUsageCount(string couponCode, string userId)
        {
            var completeOrderStatusId = (int)OrderStatus.Complete;
            var sql = @"SELECT COUNT (*) FROM ""order""
                        WHERE ""couponCode"" = @couponCode
                            AND ""userId"" = @userId
                            AND ""orderStatusId"" = @completeOrderStatusId";
            var result = await _dbHelper.Query<int>(sql, new { couponCode, userId, completeOrderStatusId });
            return result.FirstOrDefault();
        }

        public async Task UpdateCoupon(Coupon coupon)
        {
            var sql = @"UPDATE ""coupon""
                        SET ""startDate"" = @startDate,
                            ""expireDate"" = @expireDate,
                            ""productId"" = @productId,
                            ""couponTypeId"" = @couponTypeId,
                            ""amount"" = @amount,
                            ""userUsageLimit"" = @userUsageLimit,
                            ""description"" = @description,
                            ""totalUsageLimit"" = @totalUsageLimit,
                            ""applyToMultipleOrderItems"" = @applyToMultipleOrderItems
                        WHERE ""id"" = @id";

            await _dbHelper.Execute(sql, coupon);
        }
    }
}
