using Services.Helpers.Interfaces;
using Services.Repositories.Data.ReportData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class ReportsRepository : IReportsRepository
    {
        private readonly IDbHelper _db;

        public ReportsRepository(IDbHelper db) => _db = db;

        public async Task<SalesTotals> GetDayPassTotals(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT
                    COALESCE(SUM(CASE WHEN status IN ('paid','redeemed') THEN amount_cents ELSE 0 END), 0) AS RevenueCents,
                    COALESCE(SUM(CASE WHEN status IN ('paid','redeemed') THEN quantity ELSE 0 END), 0)::int AS SoldCount,
                    COALESCE(SUM(CASE WHEN status = 'refunded' THEN 1 ELSE 0 END), 0)::int AS RefundedCount,
                    COALESCE(SUM(CASE WHEN status = 'cancelled' THEN 1 ELSE 0 END), 0)::int AS CancelledCount,
                    COALESCE(SUM(CASE WHEN status = 'refunded' THEN amount_cents ELSE 0 END), 0) AS RefundedCents
                FROM day_pass_purchase
                WHERE tenant_id = @tenantId AND created_at >= @fromUtc AND created_at < @toUtc";
            var r = await _db.Query<SalesTotals>(sql, new { tenantId, fromUtc, toUtc });
            return r.FirstOrDefault() ?? new SalesTotals();
        }

        public async Task<SalesTotals> GetTicketTotals(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT
                    COALESCE(SUM(CASE WHEN status IN ('paid','redeemed') THEN amount_cents ELSE 0 END), 0) AS RevenueCents,
                    COALESCE(SUM(CASE WHEN status IN ('paid','redeemed') THEN 1 ELSE 0 END), 0)::int AS SoldCount,
                    COALESCE(SUM(CASE WHEN status = 'refunded' THEN 1 ELSE 0 END), 0)::int AS RefundedCount,
                    COALESCE(SUM(CASE WHEN status = 'cancelled' THEN 1 ELSE 0 END), 0)::int AS CancelledCount,
                    COALESCE(SUM(CASE WHEN status = 'refunded' THEN amount_cents ELSE 0 END), 0) AS RefundedCents
                FROM event_ticket_purchase
                WHERE tenant_id = @tenantId AND created_at >= @fromUtc AND created_at < @toUtc";
            var r = await _db.Query<SalesTotals>(sql, new { tenantId, fromUtc, toUtc });
            return r.FirstOrDefault() ?? new SalesTotals();
        }

        public async Task<int> GetUniqueRiders(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            // Union distinct purchaser emails across day passes and tickets within the range.
            const string sql = @"
                SELECT COUNT(DISTINCT email)::int FROM (
                    SELECT LOWER(purchaser_email) AS email FROM day_pass_purchase
                    WHERE tenant_id = @tenantId AND status IN ('paid','redeemed')
                      AND created_at >= @fromUtc AND created_at < @toUtc
                    UNION
                    SELECT LOWER(purchaser_email) AS email FROM event_ticket_purchase
                    WHERE tenant_id = @tenantId AND status IN ('paid','redeemed')
                      AND created_at >= @fromUtc AND created_at < @toUtc
                ) s";
            var r = await _db.Query<int>(sql, new { tenantId, fromUtc, toUtc });
            return r.FirstOrDefault();
        }

        public async Task<int> GetDisputeCount(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT COUNT(*)::int FROM dispute
                WHERE tenant_id = @tenantId
                  AND stripe_created_at >= @fromUtc AND stripe_created_at < @toUtc";
            var r = await _db.Query<int>(sql, new { tenantId, fromUtc, toUtc });
            return r.FirstOrDefault();
        }

        public async Task<List<DailyRevenuePoint>> GetDailyRevenue(Guid tenantId, DateTime fromUtc, DateTime toUtc, string timezone)
        {
            const string sql = @"
                SELECT date AS Date,
                       SUM(revenue)::bigint AS RevenueCents,
                       SUM(passes)::int AS PassesSold,
                       SUM(tickets)::int AS TicketsSold
                FROM (
                    SELECT to_char((created_at AT TIME ZONE @timezone)::date, 'YYYY-MM-DD') AS date,
                           amount_cents AS revenue,
                           quantity AS passes,
                           0 AS tickets
                    FROM day_pass_purchase
                    WHERE tenant_id = @tenantId AND status IN ('paid','redeemed')
                      AND created_at >= @fromUtc AND created_at < @toUtc
                    UNION ALL
                    SELECT to_char((created_at AT TIME ZONE @timezone)::date, 'YYYY-MM-DD') AS date,
                           amount_cents AS revenue,
                           0 AS passes,
                           1 AS tickets
                    FROM event_ticket_purchase
                    WHERE tenant_id = @tenantId AND status IN ('paid','redeemed')
                      AND created_at >= @fromUtc AND created_at < @toUtc
                ) s
                GROUP BY date
                ORDER BY date";
            var r = await _db.Query<DailyRevenuePoint>(sql, new { tenantId, fromUtc, toUtc, timezone });
            return r.ToList();
        }

        public async Task<List<TopDayPassProductRow>> GetTopDayPassProducts(Guid tenantId, DateTime fromUtc, DateTime toUtc, int limit = 10)
        {
            const string sql = @"
                SELECT p.id AS ProductId, p.name AS ProductName,
                       COALESCE(SUM(dpp.quantity), 0)::int AS SoldCount,
                       COALESCE(SUM(dpp.amount_cents), 0)::bigint AS RevenueCents
                FROM day_pass_purchase dpp
                JOIN day_pass_product p ON p.id = dpp.product_id
                WHERE dpp.tenant_id = @tenantId AND dpp.status IN ('paid','redeemed')
                  AND dpp.created_at >= @fromUtc AND dpp.created_at < @toUtc
                GROUP BY p.id, p.name
                ORDER BY RevenueCents DESC
                LIMIT @limit";
            var r = await _db.Query<TopDayPassProductRow>(sql, new { tenantId, fromUtc, toUtc, limit });
            return r.ToList();
        }

        public async Task<List<TopEventRow>> GetTopEvents(Guid tenantId, DateTime fromUtc, DateTime toUtc, int limit = 10)
        {
            // Combine ticket sales + reservations (day passes tied to an event) per event.
            const string sql = @"
                SELECT e.id AS EventId, e.title AS EventTitle, e.starts_at_utc AS EventStartUtc,
                       SUM(sold)::int AS SoldCount,
                       SUM(revenue)::bigint AS RevenueCents
                FROM (
                    SELECT ett.event_id AS event_id,
                           1 AS sold,
                           etp.amount_cents AS revenue
                    FROM event_ticket_purchase etp
                    JOIN event_ticket_tier ett ON ett.id = etp.tier_id
                    WHERE etp.tenant_id = @tenantId AND etp.status IN ('paid','redeemed')
                      AND etp.created_at >= @fromUtc AND etp.created_at < @toUtc
                    UNION ALL
                    SELECT dpp.event_id AS event_id,
                           dpp.quantity AS sold,
                           dpp.amount_cents AS revenue
                    FROM day_pass_purchase dpp
                    WHERE dpp.tenant_id = @tenantId AND dpp.status IN ('paid','redeemed')
                      AND dpp.event_id IS NOT NULL
                      AND dpp.created_at >= @fromUtc AND dpp.created_at < @toUtc
                ) s
                JOIN event e ON e.id = s.event_id
                GROUP BY e.id, e.title, e.starts_at_utc
                ORDER BY RevenueCents DESC
                LIMIT @limit";
            var r = await _db.Query<TopEventRow>(sql, new { tenantId, fromUtc, toUtc, limit });
            return r.ToList();
        }

        public async Task<PlatformSalesTotals> GetPlatformTotals(DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT
                    (SELECT COALESCE(SUM(amount_cents), 0) FROM day_pass_purchase
                        WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc)
                  + (SELECT COALESCE(SUM(amount_cents), 0) FROM event_ticket_purchase
                        WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc)
                  AS RevenueCents,
                    (SELECT COALESCE(SUM(quantity), 0) FROM day_pass_purchase
                        WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc)::int
                  AS PassesSold,
                    (SELECT COUNT(*) FROM event_ticket_purchase
                        WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc)::int
                  AS TicketsSold,
                    (SELECT COUNT(*) FROM day_pass_purchase
                        WHERE status = 'refunded' AND created_at >= @fromUtc AND created_at < @toUtc)::int
                  + (SELECT COUNT(*) FROM event_ticket_purchase
                        WHERE status = 'refunded' AND created_at >= @fromUtc AND created_at < @toUtc)::int
                  AS RefundedCount,
                    (SELECT COUNT(*) FROM dispute
                        WHERE stripe_created_at >= @fromUtc AND stripe_created_at < @toUtc)::int
                  AS DisputedCount,
                    (SELECT COUNT(*) FROM tenant)::int AS TotalTenants,
                    (SELECT COUNT(DISTINCT tenant_id) FROM (
                        SELECT tenant_id FROM day_pass_purchase
                            WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc
                        UNION
                        SELECT tenant_id FROM event_ticket_purchase
                            WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc
                    ) s)::int AS ActiveTenants";
            var r = await _db.Query<PlatformSalesTotals>(sql, new { fromUtc, toUtc });
            return r.FirstOrDefault() ?? new PlatformSalesTotals();
        }

        public async Task<List<DailyRevenuePoint>> GetPlatformDailyRevenue(DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT date AS Date,
                       SUM(revenue)::bigint AS RevenueCents,
                       SUM(passes)::int AS PassesSold,
                       SUM(tickets)::int AS TicketsSold
                FROM (
                    SELECT to_char((created_at AT TIME ZONE 'UTC')::date, 'YYYY-MM-DD') AS date,
                           amount_cents AS revenue,
                           quantity AS passes,
                           0 AS tickets
                    FROM day_pass_purchase
                    WHERE status IN ('paid','redeemed')
                      AND created_at >= @fromUtc AND created_at < @toUtc
                    UNION ALL
                    SELECT to_char((created_at AT TIME ZONE 'UTC')::date, 'YYYY-MM-DD') AS date,
                           amount_cents AS revenue,
                           0 AS passes,
                           1 AS tickets
                    FROM event_ticket_purchase
                    WHERE status IN ('paid','redeemed')
                      AND created_at >= @fromUtc AND created_at < @toUtc
                ) s
                GROUP BY date
                ORDER BY date";
            var r = await _db.Query<DailyRevenuePoint>(sql, new { fromUtc, toUtc });
            return r.ToList();
        }

        public async Task<List<TenantBreakdownRow>> GetTenantBreakdown(DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT t.id AS TenantId, t.subdomain AS Subdomain, t.display_name AS DisplayName,
                       COALESCE(dp.passes_sold, 0)::int AS PassesSold,
                       COALESCE(tk.tickets_sold, 0)::int AS TicketsSold,
                       (COALESCE(dp.revenue, 0) + COALESCE(tk.revenue, 0))::bigint AS RevenueCents,
                       (COALESCE(dp.refunded, 0) + COALESCE(tk.refunded, 0))::int AS RefundedCount,
                       COALESCE(disp.disputed, 0)::int AS DisputedCount
                FROM tenant t
                LEFT JOIN (
                    SELECT tenant_id,
                           SUM(CASE WHEN status IN ('paid','redeemed') THEN quantity ELSE 0 END) AS passes_sold,
                           SUM(CASE WHEN status IN ('paid','redeemed') THEN amount_cents ELSE 0 END) AS revenue,
                           SUM(CASE WHEN status = 'refunded' THEN 1 ELSE 0 END) AS refunded
                    FROM day_pass_purchase
                    WHERE created_at >= @fromUtc AND created_at < @toUtc
                    GROUP BY tenant_id
                ) dp ON dp.tenant_id = t.id
                LEFT JOIN (
                    SELECT tenant_id,
                           SUM(CASE WHEN status IN ('paid','redeemed') THEN 1 ELSE 0 END) AS tickets_sold,
                           SUM(CASE WHEN status IN ('paid','redeemed') THEN amount_cents ELSE 0 END) AS revenue,
                           SUM(CASE WHEN status = 'refunded' THEN 1 ELSE 0 END) AS refunded
                    FROM event_ticket_purchase
                    WHERE created_at >= @fromUtc AND created_at < @toUtc
                    GROUP BY tenant_id
                ) tk ON tk.tenant_id = t.id
                LEFT JOIN (
                    SELECT tenant_id, COUNT(*) AS disputed
                    FROM dispute
                    WHERE stripe_created_at >= @fromUtc AND stripe_created_at < @toUtc
                    GROUP BY tenant_id
                ) disp ON disp.tenant_id = t.id
                ORDER BY RevenueCents DESC";
            var r = await _db.Query<TenantBreakdownRow>(sql, new { fromUtc, toUtc });
            return r.ToList();
        }
    }
}
