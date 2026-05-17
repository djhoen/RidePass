using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RecentSalesRepository : IRecentSalesRepository
    {
        private readonly IDbHelper _db;

        public RecentSalesRepository(IDbHelper db) => _db = db;

        public async Task<List<RecentSalesItem>> List(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, string? status, int limit)
        {
            // Postgres pushes the WHERE clauses into each branch of the UNION ALL
            // inside the view, so per-table indexes (e.g., the per-table
            // tenant_id + created_at indexes) are still used.
            var where = new List<string> { "tenant_id = @tenantId" };
            if (fromUtc.HasValue) where.Add("created_at >= @fromUtc");
            if (toUtc.HasValue) where.Add("created_at < @toUtc");
            if (!string.IsNullOrEmpty(status)) where.Add("status = @status");

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
                       created_at               AS CreatedAt
                FROM v_recent_sales
                WHERE {string.Join(" AND ", where)}
                ORDER BY created_at DESC
                LIMIT @limit";
            var result = await _db.Query<RecentSalesItem>(sql, new { tenantId, fromUtc, toUtc, status, limit });
            return result.ToList();
        }
    }
}
