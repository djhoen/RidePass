using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RecentSalesRepository : IRecentSalesRepository
    {
        private readonly IDbHelper _db;

        public RecentSalesRepository(IDbHelper db) => _db = db;

        public async Task<List<RecentSalesItem>> List(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, string? status, int limit,
            string? email = null, string? orderId = null)
        {
            // Postgres pushes the WHERE clauses into each branch of the UNION ALL
            // inside the view, so per-table indexes (e.g., the per-table
            // tenant_id + created_at indexes) are still used.
            var where = new List<string> { "tenant_id = @tenantId" };
            if (fromUtc.HasValue) where.Add("created_at >= @fromUtc");
            if (toUtc.HasValue) where.Add("created_at < @toUtc");
            if (!string.IsNullOrEmpty(status)) where.Add("status = @status");

            // Fuzzy, case-insensitive admin search. Email matches the purchaser email;
            // order id matches the internal purchase id, the rider-facing redemption
            // token (the "Order #"), or the Stripe payment-intent id. Wildcards in the
            // input are escaped so they match literally.
            string? emailLike = null, orderLike = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                emailLike = "%" + EscapeLike(email.Trim()) + "%";
                where.Add(@"purchaser_email ILIKE @emailLike ESCAPE '\'");
            }
            if (!string.IsNullOrWhiteSpace(orderId))
            {
                orderLike = "%" + EscapeLike(orderId.Trim().TrimStart('#')) + "%";
                where.Add(@"(id::text ILIKE @orderLike ESCAPE '\'
                            OR redemption_token::text ILIKE @orderLike ESCAPE '\'
                            OR COALESCE(stripe_payment_intent_id, '') ILIKE @orderLike ESCAPE '\')");
            }

            var sql = $@"
                SELECT kind,
                       id,
                       tenant_id                AS TenantId,
                       status,
                       amount_cents             AS AmountCents,
                       purchaser_user_id        AS PurchaserUserId,
                       purchaser_email          AS PurchaserEmail,
                       purchaser_name           AS PurchaserName,
                       stripe_payment_intent_id AS StripePaymentIntentId,
                       item_name                AS ItemName,
                       created_at               AS CreatedAt,
                       redemption_token         AS RedemptionToken
                FROM v_recent_sales
                WHERE {string.Join(" AND ", where)}
                ORDER BY created_at DESC
                LIMIT @limit";
            var result = await _db.Query<RecentSalesItem>(sql, new { tenantId, fromUtc, toUtc, status, limit, emailLike, orderLike });
            return result.ToList();
        }

        public async Task<List<RecentSalesItem>> ListOrder(Guid tenantId, string kind, Guid id)
        {
            // Resolve the anchor's PaymentIntent, then return every line sharing it (the whole
            // order). No shared intent (cash / gift-card-covered) falls back to the anchor line.
            // Both the anchor lookup and the result set are scoped to the tenant.
            const string sql = @"
                WITH anchor AS (
                    SELECT stripe_payment_intent_id AS pi
                    FROM v_recent_sales
                    WHERE tenant_id = @tenantId AND kind = @kind AND id = @id
                    LIMIT 1
                )
                SELECT v.kind,
                       v.id,
                       v.tenant_id                AS TenantId,
                       v.status,
                       v.amount_cents             AS AmountCents,
                       v.purchaser_user_id        AS PurchaserUserId,
                       v.purchaser_email          AS PurchaserEmail,
                       v.purchaser_name           AS PurchaserName,
                       v.stripe_payment_intent_id AS StripePaymentIntentId,
                       v.item_name                AS ItemName,
                       v.created_at               AS CreatedAt,
                       v.redemption_token         AS RedemptionToken
                FROM v_recent_sales v
                CROSS JOIN anchor a
                WHERE v.tenant_id = @tenantId
                  AND (
                        (a.pi IS NOT NULL AND a.pi <> '' AND v.stripe_payment_intent_id = a.pi)
                     OR ((a.pi IS NULL OR a.pi = '') AND v.kind = @kind AND v.id = @id)
                  )
                ORDER BY v.created_at, v.kind";
            var result = await _db.Query<RecentSalesItem>(sql, new { tenantId, kind, id });
            return result.ToList();
        }

        // Escape LIKE/ILIKE wildcards so user-typed search input is matched literally.
        private static string EscapeLike(string s) =>
            s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
