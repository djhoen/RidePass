using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class DisputeRepository : IDisputeRepository
    {
        private const string DisputeColumns = @"
            id, tenant_id AS TenantId,
            event_ticket_purchase_id AS EventTicketPurchaseId,
            stripe_dispute_id AS StripeDisputeId,
            stripe_payment_intent_id AS StripePaymentIntentId,
            stripe_charge_id AS StripeChargeId,
            amount_cents AS AmountCents, currency, reason, status,
            evidence_due_by AS EvidenceDueBy,
            stripe_created_at AS StripeCreatedAt,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public DisputeRepository(IDbHelper db) => _db = db;

        public async Task Upsert(Dispute d)
        {
            const string sql = @"
                INSERT INTO dispute
                    (tenant_id, event_ticket_purchase_id,
                     stripe_dispute_id, stripe_payment_intent_id, stripe_charge_id,
                     amount_cents, currency, reason, status,
                     evidence_due_by, stripe_created_at)
                VALUES
                    (@TenantId, @EventTicketPurchaseId,
                     @StripeDisputeId, @StripePaymentIntentId, @StripeChargeId,
                     @AmountCents, @Currency, @Reason, @Status,
                     @EvidenceDueBy, @StripeCreatedAt)
                ON CONFLICT (stripe_dispute_id) DO UPDATE SET
                    status = EXCLUDED.status,
                    reason = EXCLUDED.reason,
                    evidence_due_by = EXCLUDED.evidence_due_by,
                    amount_cents = EXCLUDED.amount_cents,
                    updated_at = now()";
            await _db.Execute(sql, d);
        }

        public async Task<Dispute?> GetByStripeDisputeId(string stripeDisputeId)
        {
            var sql = $"SELECT {DisputeColumns} FROM dispute WHERE stripe_dispute_id = @stripeDisputeId LIMIT 1";
            var result = await _db.Query<Dispute>(sql, new { stripeDisputeId });
            return result.FirstOrDefault();
        }

        public async Task<List<DisputeWithContext>> ListByTenant(Guid tenantId)
        {
            var sql = $@"
                SELECT d.id, d.tenant_id AS TenantId,
                       d.event_ticket_purchase_id AS EventTicketPurchaseId,
                       d.stripe_dispute_id AS StripeDisputeId,
                       d.stripe_payment_intent_id AS StripePaymentIntentId,
                       d.stripe_charge_id AS StripeChargeId,
                       d.amount_cents AS AmountCents, d.currency, d.reason, d.status,
                       d.evidence_due_by AS EvidenceDueBy,
                       d.stripe_created_at AS StripeCreatedAt,
                       d.created_at AS CreatedAt, d.updated_at AS UpdatedAt,
                       t.subdomain AS TenantSubdomain,
                       etp.purchaser_name AS PurchaserName,
                       etp.purchaser_email AS PurchaserEmail,
                       (ett.name || ' — ' || e.title) AS ItemName
                FROM dispute d
                JOIN tenant t ON t.id = d.tenant_id
                LEFT JOIN event_ticket_purchase etp ON etp.id = d.event_ticket_purchase_id
                LEFT JOIN event_ticket_tier ett ON ett.id = etp.tier_id
                LEFT JOIN event e ON e.id = ett.event_id
                WHERE d.tenant_id = @tenantId
                ORDER BY d.stripe_created_at DESC";
            var result = await _db.Query<DisputeWithContext>(sql, new { tenantId });
            return result.ToList();
        }

        public async Task<List<DisputeWithContext>> ListAllAcrossTenants()
        {
            var sql = $@"
                SELECT d.id, d.tenant_id AS TenantId,
                       d.event_ticket_purchase_id AS EventTicketPurchaseId,
                       d.stripe_dispute_id AS StripeDisputeId,
                       d.stripe_payment_intent_id AS StripePaymentIntentId,
                       d.stripe_charge_id AS StripeChargeId,
                       d.amount_cents AS AmountCents, d.currency, d.reason, d.status,
                       d.evidence_due_by AS EvidenceDueBy,
                       d.stripe_created_at AS StripeCreatedAt,
                       d.created_at AS CreatedAt, d.updated_at AS UpdatedAt,
                       t.subdomain AS TenantSubdomain,
                       etp.purchaser_name AS PurchaserName,
                       etp.purchaser_email AS PurchaserEmail,
                       (ett.name || ' — ' || e.title) AS ItemName
                FROM dispute d
                JOIN tenant t ON t.id = d.tenant_id
                LEFT JOIN event_ticket_purchase etp ON etp.id = d.event_ticket_purchase_id
                LEFT JOIN event_ticket_tier ett ON ett.id = etp.tier_id
                LEFT JOIN event e ON e.id = ett.event_id
                ORDER BY d.stripe_created_at DESC";
            var result = await _db.Query<DisputeWithContext>(sql);
            return result.ToList();
        }
    }
}
