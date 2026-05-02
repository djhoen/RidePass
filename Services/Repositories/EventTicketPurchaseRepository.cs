using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EventTicketPurchaseRepository : IEventTicketPurchaseRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, tier_id AS TierId, purchaser_user_id AS PurchaserUserId,
            stripe_payment_intent_id AS StripePaymentIntentId, amount_cents AS AmountCents,
            status, purchaser_email AS PurchaserEmail, purchaser_name AS PurchaserName,
            redemption_token AS RedemptionToken, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string WithContextColumns = Columns + @",
            tier_name AS TierName, event_id AS EventId,
            event_title AS EventTitle, event_starts_at AS EventStartsAt";

        private readonly IDbHelper _db;

        public EventTicketPurchaseRepository(IDbHelper db) => _db = db;

        public async Task<(Guid Id, Guid RedemptionToken)> Create(EventTicketPurchase p)
        {
            const string sql = @"
                INSERT INTO event_ticket_purchase
                    (tenant_id, tier_id, purchaser_user_id, amount_cents, status, purchaser_email, purchaser_name)
                VALUES
                    (@TenantId, @TierId, @PurchaserUserId, @AmountCents, @Status, @PurchaserEmail, @PurchaserName)
                RETURNING id, redemption_token AS RedemptionToken";
            var row = (await _db.Query<EventTicketPurchase>(sql, p)).First();
            return (row.Id, row.RedemptionToken);
        }

        public async Task<EventTicketPurchase?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM event_ticket_purchase WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            var result = await _db.Query<EventTicketPurchase>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<EventTicketPurchase?> GetByStripePaymentIntentId(string paymentIntentId)
        {
            var sql = $"SELECT {Columns} FROM event_ticket_purchase WHERE stripe_payment_intent_id = @paymentIntentId LIMIT 1";
            var result = await _db.Query<EventTicketPurchase>(sql, new { paymentIntentId });
            return result.FirstOrDefault();
        }

        public async Task<List<EventTicketPurchase>> ListByStripePaymentIntentId(string paymentIntentId)
        {
            var sql = $"SELECT {Columns} FROM event_ticket_purchase WHERE stripe_payment_intent_id = @paymentIntentId";
            var result = await _db.Query<EventTicketPurchase>(sql, new { paymentIntentId });
            return result.ToList();
        }

        public async Task<EventTicketPurchaseWithContext?> GetByRedemptionToken(Guid token, Guid tenantId)
        {
            const string sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.tier_id AS TierId, p.purchaser_user_id AS PurchaserUserId,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       t.name AS TierName,
                       e.id AS EventId, e.title AS EventTitle, e.description AS EventDescription,
                       e.location_label AS EventLocationLabel,
                       e.starts_at AS EventStartsAt, e.ends_at AS EventEndsAt, e.all_day AS EventAllDay
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.redemption_token = @token AND p.tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<EventTicketPurchaseWithContext>(sql, new { token, tenantId });
            return result.FirstOrDefault();
        }

        public async Task SetStripePaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE event_ticket_purchase SET stripe_payment_intent_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        public async Task UpdateStatus(Guid id, string status)
        {
            const string sql = "UPDATE event_ticket_purchase SET status = @status WHERE id = @id";
            await _db.Execute(sql, new { id, status });
        }

        public async Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason)
        {
            const string sql = @"
                UPDATE event_ticket_purchase
                SET status = 'cancelled',
                    cancellation_reason = @reason,
                    cancelled_at = now(),
                    cancelled_by_user_id = @cancelledByUserId
                WHERE id = @id AND tenant_id = @tenantId AND status = 'paid'";
            await _db.Execute(sql, new { id, tenantId, cancelledByUserId, reason });
        }

        public async Task MarkRefunded(Guid id, string? refundNote)
        {
            const string sql = "UPDATE event_ticket_purchase SET status = 'refunded', refund_note = @refundNote WHERE id = @id";
            await _db.Execute(sql, new { id, refundNote });
        }

        public async Task<List<EventTicketPurchaseWithContext>> ListByStatusAcrossTenants(string status)
        {
            const string sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.tier_id AS TierId, p.purchaser_user_id AS PurchaserUserId,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.cancellation_reason AS CancellationReason, p.cancelled_at AS CancelledAt,
                       p.cancelled_by_user_id AS CancelledByUserId, p.refund_note AS RefundNote,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       t.name AS TierName, e.id AS EventId, e.title AS EventTitle,
                       e.description AS EventDescription, e.location_label AS EventLocationLabel,
                       e.starts_at AS EventStartsAt, e.ends_at AS EventEndsAt, e.all_day AS EventAllDay
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.status = @status
                ORDER BY p.cancelled_at DESC NULLS LAST, p.created_at DESC";
            var rows = await _db.Query<EventTicketPurchaseWithContext>(sql, new { status });
            return rows.ToList();
        }

        public async Task<List<EventTicketPurchaseWithContext>> GetForUser(Guid userId, Guid tenantId)
        {
            const string sql = @"
                SELECT p.id, p.tenant_id AS TenantId, p.tier_id AS TierId, p.purchaser_user_id AS PurchaserUserId,
                       p.stripe_payment_intent_id AS StripePaymentIntentId, p.amount_cents AS AmountCents,
                       p.status, p.purchaser_email AS PurchaserEmail, p.purchaser_name AS PurchaserName,
                       p.redemption_token AS RedemptionToken,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt,
                       t.name AS TierName, e.id AS EventId, e.title AS EventTitle, e.starts_at AS EventStartsAt
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id
                WHERE p.purchaser_user_id = @userId AND p.tenant_id = @tenantId
                ORDER BY p.created_at DESC";
            var rows = await _db.Query<EventTicketPurchaseWithContext>(sql, new { userId, tenantId });
            return rows.ToList();
        }
    }
}
