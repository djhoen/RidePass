using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RecentSalesRepository : IRecentSalesRepository
    {
        private readonly IDbHelper _db;

        public RecentSalesRepository(IDbHelper db) => _db = db;

        public async Task<(List<RecentSalesItem> Rows, int Total)> List(Guid tenantId, DateTime? fromUtc, DateTime? toUtc,
            IReadOnlyCollection<string>? statuses, IReadOnlyCollection<string>? kinds, int offset, int limit,
            string? email = null, string? orderId = null, bool includeAbandoned = false)
        {
            // Postgres pushes the WHERE clauses into each branch of the UNION ALL
            // inside the view, so per-table indexes (e.g., the per-table
            // tenant_id + created_at indexes) are still used.
            var where = new List<string> { "tenant_id = @tenantId" };
            if (fromUtc.HasValue) where.Add("created_at >= @fromUtc");
            if (toUtc.HasValue) where.Add("created_at < @toUtc");

            // 'abandoned' is our own reconciler giving up on a checkout that never saw a
            // completed payment attempt, not a real decline. Every existing caller keeps
            // reading as before by default; a caller that explicitly asks for 'abandoned'
            // (in statuses) still gets it, and includeAbandoned lifts the exclusion without
            // naming any status. That flag exists because v_recent_sales has EIGHT branches with
            // different vocabularies (gift_card runs pending/active/depleted/refunded/void;
            // shop_rental has post-payment states out/returned/damaged), so a caller trying to say
            // "show me everything" by listing statuses would silently drop whole kinds.
            // Blank entries are dropped first: an empty query-string param (?statuses=) binds as a
            // one-element list containing "", which would otherwise look like a real selection and
            // match nothing at all instead of falling back to the default view.
            var statusArray = statuses?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() is { Length: > 0 } s2 ? s2 : null;
            var excludedStatuses = statusArray is null && !includeAbandoned ? new[] { "abandoned" } : null;
            if (statusArray is not null) where.Add("status = ANY(@statusArray)");
            if (excludedStatuses is not null) where.Add("status <> ALL(@excludedStatuses)");

            var kindArray = kinds?.Where(k => !string.IsNullOrWhiteSpace(k)).ToArray() is { Length: > 0 } k2 ? k2 : null;
            if (kindArray is not null) where.Add("kind = ANY(@kindArray)");

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

            // Never trust caller-supplied paging: a negative offset or a runaway limit
            // would either error in Postgres or pull far more than a page's worth.
            var safeOffset = Math.Max(offset, 0);
            var safeLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);

            var whereClause = string.Join(" AND ", where);
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
                       redemption_token         AS RedemptionToken,
                       COUNT(*) OVER()::int     AS Total
                FROM v_recent_sales
                WHERE {whereClause}
                ORDER BY created_at DESC, id DESC
                OFFSET @safeOffset
                LIMIT @safeLimit";
            var queryParams = new
            {
                tenantId, fromUtc, toUtc, statusArray, excludedStatuses, kindArray,
                emailLike, orderLike, safeOffset, safeLimit
            };
            var mapped = (await _db.Query<RecentSalesItem, TotalCountRow, (RecentSalesItem Row, int Total)>(
                sql, (row, totalRow) => (row, totalRow.Total), queryParams, splitOn: "Total")).ToList();
            var rows = mapped.Select(m => m.Row).ToList();

            // COUNT(*) OVER() rides along with each returned row, so it only comes back when at
            // least one row survives OFFSET/LIMIT. A page request landing past the end of the
            // filtered set (stale UI paging, a filter that shrank the result after the client
            // cached a page) would otherwise report a false total of 0, so fall back to a plain
            // count in that one case.
            int total;
            if (rows.Count > 0)
            {
                total = mapped[0].Total;
            }
            else
            {
                var countSql = $"SELECT COUNT(*)::int FROM v_recent_sales WHERE {whereClause}";
                total = await _db.ExecuteScalar(countSql, queryParams);
            }

            return (rows, total);
        }

        // Multi-mapping target for the COUNT(*) OVER() column tacked onto the row query above.
        private class TotalCountRow
        {
            public int Total { get; set; }
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
