using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class PassPurchaseRepository : IPassPurchaseRepository
    {
        private const string PurchaseColumns = @"
            id, tenant_id AS TenantId, purchaser_user_id AS PurchaserUserId, product_id AS ProductId,
            waiver_signature_id AS WaiverSignatureId, valid_on_date AS ValidOnDate,
            stripe_payment_intent_id AS StripePaymentIntentId, amount_cents AS AmountCents,
            service_charge_cents AS ServiceChargeCents,
            applied_reward_redemption_id AS AppliedRewardRedemptionId,
            payment_method AS PaymentMethod,
            status, purchaser_email AS PurchaserEmail, purchaser_name AS PurchaserName,
            redemption_token AS RedemptionToken,
            event_id AS EventId, quantity,
            cancellation_reason AS CancellationReason, cancelled_at AS CancelledAt,
            cancelled_by_user_id AS CancelledByUserId, refund_note AS RefundNote,
            redeemed_at_utc AS RedeemedAtUtc, redeemed_by_user_id AS RedeemedByUserId,
            sold_by_user_id AS SoldByUserId,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public PassPurchaseRepository(IDbHelper db) => _db = db;

        public async Task<(Guid Id, Guid RedemptionToken)> Create(PassPurchase p)
        {
            const string sql = @"
                INSERT INTO pass_purchase
                    (tenant_id, purchaser_user_id, product_id, waiver_signature_id, valid_on_date,
                     amount_cents, service_charge_cents, applied_reward_redemption_id, payment_method,
                     status, purchaser_email, purchaser_name, event_id, quantity, sold_by_user_id)
                VALUES
                    (@TenantId, @PurchaserUserId, @ProductId, @WaiverSignatureId, @ValidOnDate,
                     @AmountCents, @ServiceChargeCents, @AppliedRewardRedemptionId, @PaymentMethod,
                     @Status, @PurchaserEmail, @PurchaserName, @EventId, @Quantity, @SoldByUserId)
                RETURNING id, redemption_token AS RedemptionToken";
            var row = (await _db.Query<PassPurchase>(sql, p)).First();
            return (row.Id, row.RedemptionToken);
        }

        public async Task<PassPurchase?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {PurchaseColumns} FROM pass_purchase WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            var result = await _db.Query<PassPurchase>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<PassPurchase?> GetByStripePaymentIntentId(string paymentIntentId)
        {
            var sql = $@"
                SELECT {PurchaseColumns}
                FROM pass_purchase
                WHERE stripe_payment_intent_id = @paymentIntentId
                LIMIT 1";
            var result = await _db.Query<PassPurchase>(sql, new { paymentIntentId });
            return result.FirstOrDefault();
        }

        public async Task<List<PassPurchase>> ListByStripePaymentIntentId(string paymentIntentId)
        {
            var sql = $@"
                SELECT {PurchaseColumns}
                FROM pass_purchase
                WHERE stripe_payment_intent_id = @paymentIntentId";
            var result = await _db.Query<PassPurchase>(sql, new { paymentIntentId });
            return result.ToList();
        }

        public async Task<PassPurchaseWithContext?> GetByRedemptionToken(Guid token, Guid tenantId)
        {
            var sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.purchaser_user_id AS PurchaserUserId, p.product_id AS ProductId,
                       p.waiver_signature_id AS WaiverSignatureId, p.valid_on_date AS ValidOnDate,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.event_id AS EventId, p.quantity,
                       p.cancellation_reason AS CancellationReason, p.cancelled_at AS CancelledAt,
                       p.cancelled_by_user_id AS CancelledByUserId, p.refund_note AS RefundNote,
                       p.redeemed_at_utc AS RedeemedAtUtc, p.redeemed_by_user_id AS RedeemedByUserId,
                       p.sold_by_user_id AS SoldByUserId,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       pr.name AS ProductName
                FROM pass_purchase p
                JOIN pass_product pr ON pr.id = p.product_id
                WHERE p.redemption_token = @token AND p.tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<PassPurchaseWithContext>(sql, new { token, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<List<PassPurchaseWithContext>> GetForUser(Guid userId, Guid tenantId)
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
                       p.redeemed_at_utc AS RedeemedAtUtc, p.redeemed_by_user_id AS RedeemedByUserId,
                       p.sold_by_user_id AS SoldByUserId,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       pr.name AS ProductName
                FROM pass_purchase p
                JOIN pass_product pr ON pr.id = p.product_id
                WHERE p.purchaser_user_id = @userId AND p.tenant_id = @tenantId
                ORDER BY p.created_at DESC";
            var result = await _db.Query<PassPurchaseWithContext>(sql, new { userId, tenantId });
            return result.ToList();
        }

        public async Task SetStripePaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE pass_purchase SET stripe_payment_intent_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        public async Task UpdateStatus(Guid id, string status)
        {
            const string sql = "UPDATE pass_purchase SET status = @status WHERE id = @id";
            await _db.Execute(sql, new { id, status });
        }

        public async Task MarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc)
        {
            // tenant_id predicate prevents a stray purchaseId from another tenant being
            // flipped to redeemed. UndoRedeemed already had this; MarkRedeemed didn't.
            const string sql = @"
                UPDATE pass_purchase
                SET status = 'redeemed', redeemed_at_utc = @atUtc, redeemed_by_user_id = @redeemedByUserId
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, redeemedByUserId, atUtc });
        }

        public async Task UndoRedeemed(Guid id, Guid tenantId)
        {
            const string sql = @"
                UPDATE pass_purchase
                SET status = 'paid', redeemed_at_utc = NULL, redeemed_by_user_id = NULL
                WHERE id = @id AND tenant_id = @tenantId AND status = 'redeemed'";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason)
        {
            const string sql = @"
                UPDATE pass_purchase
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
                UPDATE pass_purchase
                SET status = 'refunded', refund_note = @refundNote
                WHERE id = @id";
            await _db.Execute(sql, new { id, refundNote });
        }

        public async Task<int> ActiveSpotsReservedForEvent(Guid eventId)
        {
            // Active statuses hold a spot; cancelled/refunded/failed release the spot.
            const string sql = @"
                SELECT COALESCE(SUM(quantity), 0)
                FROM pass_purchase
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
                FROM pass_purchase
                WHERE event_id = ANY(@ids)
                  AND status IN ('pending', 'paid', 'redeemed')
                GROUP BY event_id";
            var rows = await _db.Query<(Guid EventId, long SoldQuantity)>(sql, new { ids });
            return rows.ToDictionary(r => r.EventId, r => (int)r.SoldQuantity);
        }

        public async Task<List<PassPurchaseWithContext>> ListForAdmin(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, string? status)
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
                       p.redeemed_at_utc AS RedeemedAtUtc, p.redeemed_by_user_id AS RedeemedByUserId,
                       p.sold_by_user_id AS SoldByUserId,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       pr.name AS ProductName
                FROM pass_purchase p
                JOIN pass_product pr ON pr.id = p.product_id
                WHERE {string.Join(" AND ", where)}
                ORDER BY p.created_at DESC";

            var result = await _db.Query<PassPurchaseWithContext>(sql, new { tenantId, fromUtc, toUtc, status });
            return result.ToList();
        }

        public async Task<List<PassPurchaseWithContext>> ListByStatusAcrossTenants(string status)
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
                       p.redeemed_at_utc AS RedeemedAtUtc, p.redeemed_by_user_id AS RedeemedByUserId,
                       p.sold_by_user_id AS SoldByUserId,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       pr.name AS ProductName
                FROM pass_purchase p
                JOIN pass_product pr ON pr.id = p.product_id
                WHERE p.status = @status
                ORDER BY p.cancelled_at DESC NULLS LAST, p.created_at DESC";
            var rows = await _db.Query<PassPurchaseWithContext>(sql, new { status });
            return rows.ToList();
        }
    }
}
