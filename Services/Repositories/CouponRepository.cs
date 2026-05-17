using Services.Helpers.Interfaces;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class CouponRepository : ICouponRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, code, description,
            discount_kind AS DiscountKind, discount_value AS DiscountValue,
            applicable_scope AS ApplicableScope, applicable_event_id AS ApplicableEventId,
            valid_from_utc AS ValidFromUtc, valid_to_utc AS ValidToUtc,
            max_total_uses AS MaxTotalUses, max_uses_per_user AS MaxUsesPerUser,
            is_active AS IsActive,
            created_by_user_id AS CreatedByUserId,
            issued_to_user_id AS IssuedToUserId,
            issued_from_purchase_id AS IssuedFromPurchaseId,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public CouponRepository(IDbHelper db) => _db = db;

        public async Task<List<Coupon>> ListByTenant(Guid tenantId)
        {
            var sql = $@"
                SELECT {Columns}
                FROM coupon
                WHERE tenant_id = @tenantId
                ORDER BY is_active DESC, created_at DESC";
            return (await _db.Query<Coupon>(sql, new { tenantId })).ToList();
        }

        public async Task<Coupon?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM coupon WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<Coupon>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Coupon?> GetByCode(Guid tenantId, string code)
        {
            // Case-insensitive lookup matches the unique-index expression.
            var sql = $"SELECT {Columns} FROM coupon WHERE tenant_id = @tenantId AND lower(code) = lower(@code) LIMIT 1";
            return (await _db.Query<Coupon>(sql, new { tenantId, code })).FirstOrDefault();
        }

        public async Task<Guid> Create(Coupon c)
        {
            const string sql = @"
                INSERT INTO coupon
                    (tenant_id, code, description, discount_kind, discount_value,
                     applicable_scope, applicable_event_id, valid_from_utc, valid_to_utc,
                     max_total_uses, max_uses_per_user, is_active, created_by_user_id,
                     issued_to_user_id, issued_from_purchase_id)
                VALUES
                    (@TenantId, @Code, @Description, @DiscountKind, @DiscountValue,
                     @ApplicableScope, @ApplicableEventId, @ValidFromUtc, @ValidToUtc,
                     @MaxTotalUses, @MaxUsesPerUser, @IsActive, @CreatedByUserId,
                     @IssuedToUserId, @IssuedFromPurchaseId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, c)).First();
        }

        public async Task Update(Coupon c)
        {
            const string sql = @"
                UPDATE coupon
                SET code = @Code,
                    description = @Description,
                    discount_kind = @DiscountKind,
                    discount_value = @DiscountValue,
                    applicable_scope = @ApplicableScope,
                    applicable_event_id = @ApplicableEventId,
                    valid_from_utc = @ValidFromUtc,
                    valid_to_utc = @ValidToUtc,
                    max_total_uses = @MaxTotalUses,
                    max_uses_per_user = @MaxUsesPerUser,
                    is_active = @IsActive
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, c);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            // Hard delete OK because coupon_redemption FK is ON DELETE CASCADE.
            const string sql = "DELETE FROM coupon WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task<int> CountRedemptions(Guid couponId)
        {
            const string sql = "SELECT COUNT(*) FROM coupon_redemption WHERE coupon_id = @couponId";
            return await _db.ExecuteScalar(sql, new { couponId });
        }

        public async Task<int> CountUserRedemptions(Guid couponId, Guid userId)
        {
            const string sql = @"
                SELECT COUNT(*) FROM coupon_redemption
                WHERE coupon_id = @couponId AND user_id = @userId";
            return await _db.ExecuteScalar(sql, new { couponId, userId });
        }

        public async Task<List<Coupon>> ListIssuedToUser(Guid userId, Guid tenantId)
        {
            // Issued-to-user coupons across all this user's purchases at this tenant.
            var sql = $@"
                SELECT {Columns}
                FROM coupon
                WHERE tenant_id = @tenantId AND issued_to_user_id = @userId
                ORDER BY created_at DESC";
            return (await _db.Query<Coupon>(sql, new { tenantId, userId })).ToList();
        }

        public async Task<List<Coupon>> ListIssuedFromPurchase(Guid purchaseId)
        {
            var sql = $@"
                SELECT {Columns}
                FROM coupon
                WHERE issued_from_purchase_id = @purchaseId
                ORDER BY created_at";
            return (await _db.Query<Coupon>(sql, new { purchaseId })).ToList();
        }

        public async Task<Guid> RecordRedemption(CouponRedemption r)
        {
            const string sql = @"
                INSERT INTO coupon_redemption
                    (coupon_id, tenant_id, user_id, source_kind, source_id, discount_cents)
                VALUES
                    (@CouponId, @TenantId, @UserId, @SourceKind, @SourceId, @DiscountCents)
                RETURNING id";
            return (await _db.Query<Guid>(sql, r)).First();
        }

        public async Task<Guid> RecordShare(CouponShare s)
        {
            const string sql = @"
                INSERT INTO coupon_share
                    (coupon_id, tenant_id, sender_user_id, recipient_email, recipient_name, personal_note)
                VALUES
                    (@CouponId, @TenantId, @SenderUserId, @RecipientEmail, @RecipientName, @PersonalNote)
                RETURNING id";
            return (await _db.Query<Guid>(sql, s)).First();
        }

        private const string ShareColumns = @"
            id, coupon_id AS CouponId, tenant_id AS TenantId,
            sender_user_id AS SenderUserId,
            recipient_email AS RecipientEmail, recipient_name AS RecipientName,
            personal_note AS PersonalNote,
            sent_at AS SentAt, redeemed_at AS RedeemedAt";

        public async Task<List<CouponShare>> ListSharesByCoupon(Guid couponId)
        {
            var sql = $@"
                SELECT {ShareColumns}
                FROM coupon_share
                WHERE coupon_id = @couponId
                ORDER BY sent_at DESC";
            return (await _db.Query<CouponShare>(sql, new { couponId })).ToList();
        }

        public async Task<List<CouponShare>> ListSharesByTenant(Guid tenantId, int take = 1000)
        {
            var sql = $@"
                SELECT {ShareColumns}
                FROM coupon_share
                WHERE tenant_id = @tenantId
                ORDER BY sent_at DESC
                LIMIT @take";
            return (await _db.Query<CouponShare>(sql, new { tenantId, take })).ToList();
        }
    }
}
