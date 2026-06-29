using Services.Helpers.Interfaces;
using Services.Repositories.Data.MembershipData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class MembershipRepository : IMembershipRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, user_id AS UserId,
            name_at_purchase AS NameAtPurchase,
            price_cents AS PriceCents,
            duration_kind AS DurationKind,
            valid_from_utc AS ValidFromUtc,
            valid_to_utc AS ValidToUtc,
            amount_cents AS AmountCents,
            service_charge_cents AS ServiceChargeCents,
            payment_method AS PaymentMethod,
            stripe_payment_intent_id AS StripePaymentIntentId,
            stripe_connected_account_id AS StripeConnectedAccountId,
            status,
            cancelled_reason AS CancelledReason,
            cancelled_by_user_id AS CancelledByUserId,
            cancelled_at AS CancelledAt,
            sold_by_user_id AS SoldByUserId,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;
        public MembershipRepository(IDbHelper db) => _db = db;

        public async Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason)
        {
            const string sql = @"
                UPDATE membership_purchase
                SET status = 'cancelled',
                    cancelled_reason = @reason,
                    cancelled_at = now(),
                    cancelled_by_user_id = @cancelledByUserId
                WHERE id = @id AND tenant_id = @tenantId AND status = 'paid'";
            await _db.Execute(sql, new { id, tenantId, cancelledByUserId, reason });
        }

        public async Task MarkRefunded(Guid id)
        {
            const string sql = "UPDATE membership_purchase SET status = 'refunded' WHERE id = @id";
            await _db.Execute(sql, new { id });
        }

        public async Task<Guid> Create(MembershipPurchase p)
        {
            const string sql = @"
                INSERT INTO membership_purchase
                    (tenant_id, user_id, name_at_purchase, price_cents, duration_kind,
                     valid_from_utc, valid_to_utc, amount_cents, service_charge_cents,
                     payment_method, status, sold_by_user_id)
                VALUES
                    (@TenantId, @UserId, @NameAtPurchase, @PriceCents, @DurationKind,
                     @ValidFromUtc, @ValidToUtc, @AmountCents, @ServiceChargeCents,
                     @PaymentMethod, @Status, @SoldByUserId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        public async Task<MembershipPurchase?> GetById(Guid id)
        {
            var sql = $"SELECT {Columns} FROM membership_purchase WHERE id = @id LIMIT 1";
            return (await _db.Query<MembershipPurchase>(sql, new { id })).FirstOrDefault();
        }

        public async Task<MembershipPurchase?> GetByPaymentIntentId(string paymentIntentId)
        {
            var sql = $@"SELECT {Columns} FROM membership_purchase
                         WHERE stripe_payment_intent_id = @paymentIntentId LIMIT 1";
            return (await _db.Query<MembershipPurchase>(sql, new { paymentIntentId })).FirstOrDefault();
        }

        public async Task<MembershipPurchase?> GetActive(Guid userId, Guid tenantId, DateTime nowUtc)
        {
            // Active = paid AND (lifetime OR valid_to_utc still in the future).
            // Order by valid_to nulls-first so a lifetime row beats a yearly row in the rare case both exist.
            var sql = $@"
                SELECT {Columns} FROM membership_purchase
                WHERE user_id = @userId
                  AND tenant_id = @tenantId
                  AND status = 'paid'
                  AND (valid_to_utc IS NULL OR valid_to_utc > @nowUtc)
                ORDER BY valid_to_utc IS NULL DESC, valid_to_utc DESC
                LIMIT 1";
            return (await _db.Query<MembershipPurchase>(sql, new { userId, tenantId, nowUtc })).FirstOrDefault();
        }

        public async Task<List<MembershipPurchase>> ListMine(Guid userId, Guid tenantId)
        {
            var sql = $@"
                SELECT {Columns} FROM membership_purchase
                WHERE user_id = @userId AND tenant_id = @tenantId
                ORDER BY created_at DESC";
            return (await _db.Query<MembershipPurchase>(sql, new { userId, tenantId })).ToList();
        }

        public async Task<List<MembershipPurchase>> ListForTenant(Guid tenantId)
        {
            var sql = $@"
                SELECT {Columns} FROM membership_purchase
                WHERE tenant_id = @tenantId
                ORDER BY created_at DESC";
            return (await _db.Query<MembershipPurchase>(sql, new { tenantId })).ToList();
        }

        public async Task SetStripePaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE membership_purchase SET stripe_payment_intent_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        // Direct charge: snapshot the connected account this membership was charged on (bundled onto a
        // direct event-ticket cart) and flag the row so refunds act on the right account.
        public async Task MarkDirectCharge(Guid id, Guid tenantId, string connectedAccountId)
        {
            const string sql = @"
                UPDATE membership_purchase
                SET stripe_connected_account_id = @connectedAccountId,
                    payment_method = 'stripe_direct'
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, connectedAccountId });
        }

        public async Task UpdateStatus(Guid id, string status)
        {
            const string sql = "UPDATE membership_purchase SET status = @status WHERE id = @id";
            await _db.Execute(sql, new { id, status });
        }
    }
}
