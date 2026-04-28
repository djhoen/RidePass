using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class DayPassPurchaseRepository : IDayPassPurchaseRepository
    {
        private const string PurchaseColumns = @"
            id, tenant_id AS TenantId, purchaser_user_id AS PurchaserUserId, product_id AS ProductId,
            waiver_signature_id AS WaiverSignatureId, valid_on_date AS ValidOnDate,
            stripe_payment_intent_id AS StripePaymentIntentId, amount_cents AS AmountCents,
            status, purchaser_email AS PurchaserEmail, purchaser_name AS PurchaserName,
            redemption_token AS RedemptionToken,
            event_id AS EventId, quantity,
            cancellation_reason AS CancellationReason, cancelled_at AS CancelledAt,
            cancelled_by_user_id AS CancelledByUserId, refund_note AS RefundNote,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public DayPassPurchaseRepository(IDbHelper db) => _db = db;

        public async Task<(Guid Id, Guid RedemptionToken)> Create(DayPassPurchase p)
        {
            const string sql = @"
                INSERT INTO day_pass_purchase
                    (tenant_id, purchaser_user_id, product_id, waiver_signature_id, valid_on_date,
                     amount_cents, status, purchaser_email, purchaser_name, event_id, quantity)
                VALUES
                    (@TenantId, @PurchaserUserId, @ProductId, @WaiverSignatureId, @ValidOnDate,
                     @AmountCents, @Status, @PurchaserEmail, @PurchaserName, @EventId, @Quantity)
                RETURNING id, redemption_token AS RedemptionToken";
            var row = (await _db.Query<DayPassPurchase>(sql, p)).First();
            return (row.Id, row.RedemptionToken);
        }

        public async Task<DayPassPurchase?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {PurchaseColumns} FROM day_pass_purchase WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            var result = await _db.Query<DayPassPurchase>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<DayPassPurchase?> GetByStripePaymentIntentId(string paymentIntentId)
        {
            var sql = $@"
                SELECT {PurchaseColumns}
                FROM day_pass_purchase
                WHERE stripe_payment_intent_id = @paymentIntentId
                LIMIT 1";
            var result = await _db.Query<DayPassPurchase>(sql, new { paymentIntentId });
            return result.FirstOrDefault();
        }

        public async Task<DayPassPurchaseWithContext?> GetByRedemptionToken(Guid token, Guid tenantId)
        {
            var sql = $@"
                SELECT {PurchaseColumns.Replace(Environment.NewLine, Environment.NewLine + "                       ").Replace(", id,", ", p.id,").Replace("            id,", "            p.id,")},
                       pr.name AS ProductName
                FROM day_pass_purchase p
                JOIN day_pass_product pr ON pr.id = p.product_id
                WHERE p.redemption_token = @token AND p.tenant_id = @tenantId
                LIMIT 1";
            // Simpler: write columns explicitly to avoid string-surgery
            sql = $@"
                SELECT p.id, p.tenant_id AS TenantId, p.purchaser_user_id AS PurchaserUserId, p.product_id AS ProductId,
                       p.waiver_signature_id AS WaiverSignatureId, p.valid_on_date AS ValidOnDate,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.event_id AS EventId, p.quantity,
                       p.cancellation_reason AS CancellationReason, p.cancelled_at AS CancelledAt,
                       p.cancelled_by_user_id AS CancelledByUserId, p.refund_note AS RefundNote,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       pr.name AS ProductName
                FROM day_pass_purchase p
                JOIN day_pass_product pr ON pr.id = p.product_id
                WHERE p.redemption_token = @token AND p.tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<DayPassPurchaseWithContext>(sql, new { token, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<List<DayPassPurchaseWithContext>> GetForUser(Guid userId, Guid tenantId)
        {
            const string sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.purchaser_user_id AS PurchaserUserId, p.product_id AS ProductId,
                       p.waiver_signature_id AS WaiverSignatureId, p.valid_on_date AS ValidOnDate,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.event_id AS EventId, p.quantity,
                       p.cancellation_reason AS CancellationReason, p.cancelled_at AS CancelledAt,
                       p.cancelled_by_user_id AS CancelledByUserId, p.refund_note AS RefundNote,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       pr.name AS ProductName
                FROM day_pass_purchase p
                JOIN day_pass_product pr ON pr.id = p.product_id
                WHERE p.purchaser_user_id = @userId AND p.tenant_id = @tenantId
                ORDER BY p.created_at DESC";
            var result = await _db.Query<DayPassPurchaseWithContext>(sql, new { userId, tenantId });
            return result.ToList();
        }

        public async Task SetStripePaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE day_pass_purchase SET stripe_payment_intent_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        public async Task UpdateStatus(Guid id, string status)
        {
            const string sql = "UPDATE day_pass_purchase SET status = @status WHERE id = @id";
            await _db.Execute(sql, new { id, status });
        }

        public async Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason)
        {
            const string sql = @"
                UPDATE day_pass_purchase
                SET status = 'cancelled',
                    cancellation_reason = @reason,
                    cancelled_at = now(),
                    cancelled_by_user_id = @cancelledByUserId
                WHERE id = @id AND tenant_id = @tenantId AND status = 'paid'";
            await _db.Execute(sql, new { id, tenantId, cancelledByUserId, reason });
        }

        public async Task MarkRefunded(Guid id, string? refundNote)
        {
            const string sql = @"
                UPDATE day_pass_purchase
                SET status = 'refunded', refund_note = @refundNote
                WHERE id = @id";
            await _db.Execute(sql, new { id, refundNote });
        }

        public async Task<int> ActiveSpotsReservedForEvent(Guid eventId)
        {
            // Active statuses hold a spot; cancelled/refunded/failed release the spot.
            const string sql = @"
                SELECT COALESCE(SUM(quantity), 0)
                FROM day_pass_purchase
                WHERE event_id = @eventId
                  AND status IN ('pending', 'paid', 'redeemed')";
            return await _db.ExecuteScalar(sql, new { eventId });
        }

        public async Task<Dictionary<Guid, int>> ActiveSpotsReservedForEvents(IEnumerable<Guid> eventIds)
        {
            var ids = eventIds.ToArray();
            if (ids.Length == 0) return new();
            const string sql = @"
                SELECT event_id AS EventId, COALESCE(SUM(quantity), 0) AS SoldQuantity
                FROM day_pass_purchase
                WHERE event_id = ANY(@ids)
                  AND status IN ('pending', 'paid', 'redeemed')
                GROUP BY event_id";
            var rows = await _db.Query<(Guid EventId, long SoldQuantity)>(sql, new { ids });
            return rows.ToDictionary(r => r.EventId, r => (int)r.SoldQuantity);
        }

        public async Task<List<DayPassPurchaseWithContext>> ListForAdmin(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, string? status)
        {
            var where = new List<string> { "p.tenant_id = @tenantId" };
            if (fromUtc.HasValue) where.Add("p.created_at >= @fromUtc");
            if (toUtc.HasValue) where.Add("p.created_at < @toUtc");
            if (!string.IsNullOrEmpty(status)) where.Add("p.status = @status");

            var sql = $@"
                SELECT p.id, p.tenant_id AS TenantId, p.purchaser_user_id AS PurchaserUserId, p.product_id AS ProductId,
                       p.waiver_signature_id AS WaiverSignatureId, p.valid_on_date AS ValidOnDate,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.event_id AS EventId, p.quantity,
                       p.cancellation_reason AS CancellationReason, p.cancelled_at AS CancelledAt,
                       p.cancelled_by_user_id AS CancelledByUserId, p.refund_note AS RefundNote,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       pr.name AS ProductName
                FROM day_pass_purchase p
                JOIN day_pass_product pr ON pr.id = p.product_id
                WHERE {string.Join(" AND ", where)}
                ORDER BY p.created_at DESC";

            var result = await _db.Query<DayPassPurchaseWithContext>(sql, new { tenantId, fromUtc, toUtc, status });
            return result.ToList();
        }

        public async Task<List<DayPassPurchaseWithContext>> ListByStatusAcrossTenants(string status)
        {
            const string sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.purchaser_user_id AS PurchaserUserId, p.product_id AS ProductId,
                       p.waiver_signature_id AS WaiverSignatureId, p.valid_on_date AS ValidOnDate,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.event_id AS EventId, p.quantity,
                       p.cancellation_reason AS CancellationReason, p.cancelled_at AS CancelledAt,
                       p.cancelled_by_user_id AS CancelledByUserId, p.refund_note AS RefundNote,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       pr.name AS ProductName
                FROM day_pass_purchase p
                JOIN day_pass_product pr ON pr.id = p.product_id
                WHERE p.status = @status
                ORDER BY p.cancelled_at DESC NULLS LAST, p.created_at DESC";
            var rows = await _db.Query<DayPassPurchaseWithContext>(sql, new { status });
            return rows.ToList();
        }
    }
}
