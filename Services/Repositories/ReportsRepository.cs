using Services.Helpers.Interfaces;
using Services.Repositories.Data.ReportData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class ReportsRepository : IReportsRepository
    {
        private readonly IDbHelper _db;

        public ReportsRepository(IDbHelper db) => _db = db;

        public async Task<SalesTotals> GetPassTotals(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT
                    COALESCE(SUM(CASE WHEN status IN ('paid','redeemed') THEN amount_cents ELSE 0 END), 0) AS RevenueCents,
                    COALESCE(SUM(CASE WHEN status IN ('paid','redeemed') THEN quantity ELSE 0 END), 0)::int AS SoldCount,
                    COALESCE(SUM(CASE WHEN status = 'refunded' THEN 1 ELSE 0 END), 0)::int AS RefundedCount,
                    COALESCE(SUM(CASE WHEN status = 'cancelled' THEN 1 ELSE 0 END), 0)::int AS CancelledCount,
                    COALESCE(SUM(CASE WHEN status = 'refunded' THEN amount_cents ELSE 0 END), 0) AS RefundedCents
                FROM pass_purchase
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
            // Union distinct purchaser emails across passes and tickets within the range.
            const string sql = @"
                SELECT COUNT(DISTINCT email)::int FROM (
                    SELECT LOWER(purchaser_email) AS email FROM pass_purchase
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
                    FROM pass_purchase
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

        public async Task<List<TopPassProductRow>> GetTopPassProducts(Guid tenantId, DateTime fromUtc, DateTime toUtc, int limit = 10)
        {
            const string sql = @"
                SELECT p.id AS ProductId, p.name AS ProductName,
                       COALESCE(SUM(dpp.quantity), 0)::int AS SoldCount,
                       COALESCE(SUM(dpp.amount_cents), 0)::bigint AS RevenueCents
                FROM pass_purchase dpp
                JOIN pass_product p ON p.id = dpp.product_id
                WHERE dpp.tenant_id = @tenantId AND dpp.status IN ('paid','redeemed')
                  AND dpp.created_at >= @fromUtc AND dpp.created_at < @toUtc
                GROUP BY p.id, p.name
                ORDER BY RevenueCents DESC
                LIMIT @limit";
            var r = await _db.Query<TopPassProductRow>(sql, new { tenantId, fromUtc, toUtc, limit });
            return r.ToList();
        }

        public async Task<List<TopEventRow>> GetTopEvents(Guid tenantId, DateTime fromUtc, DateTime toUtc, int limit = 10)
        {
            // Combine ticket sales + reservations (passes tied to an event) per event.
            const string sql = @"
                SELECT e.id AS EventId, e.title AS EventTitle, e.starts_at AS EventStartUtc,
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
                    FROM pass_purchase dpp
                    WHERE dpp.tenant_id = @tenantId AND dpp.status IN ('paid','redeemed')
                      AND dpp.event_id IS NOT NULL
                      AND dpp.created_at >= @fromUtc AND dpp.created_at < @toUtc
                ) s
                JOIN event e ON e.id = s.event_id
                GROUP BY e.id, e.title, e.starts_at
                ORDER BY RevenueCents DESC
                LIMIT @limit";
            var r = await _db.Query<TopEventRow>(sql, new { tenantId, fromUtc, toUtc, limit });
            return r.ToList();
        }

        public async Task<PlatformSalesTotals> GetPlatformTotals(DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT
                    (SELECT COALESCE(SUM(amount_cents), 0) FROM pass_purchase
                        WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc)
                  + (SELECT COALESCE(SUM(amount_cents), 0) FROM event_ticket_purchase
                        WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc)
                  AS RevenueCents,
                    (SELECT COALESCE(SUM(quantity), 0) FROM pass_purchase
                        WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc)::int
                  AS PassesSold,
                    (SELECT COUNT(*) FROM event_ticket_purchase
                        WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc)::int
                  AS TicketsSold,
                    (SELECT COUNT(*) FROM pass_purchase
                        WHERE status = 'refunded' AND created_at >= @fromUtc AND created_at < @toUtc)::int
                  + (SELECT COUNT(*) FROM event_ticket_purchase
                        WHERE status = 'refunded' AND created_at >= @fromUtc AND created_at < @toUtc)::int
                  AS RefundedCount,
                    (SELECT COUNT(*) FROM dispute
                        WHERE stripe_created_at >= @fromUtc AND stripe_created_at < @toUtc)::int
                  AS DisputedCount,
                    (SELECT COUNT(*) FROM tenant)::int AS TotalTenants,
                    (SELECT COUNT(DISTINCT tenant_id) FROM (
                        SELECT tenant_id FROM pass_purchase
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
                    FROM pass_purchase
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
                    FROM pass_purchase
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

        // ── Event Riders / Daily Events ─────────────────────────────────────
        public async Task<List<EventRiderRow>> GetEventRiders(Guid tenantId, Guid eventId)
        {
            // UNION across the three sources of "registrant" for an event:
            //   - pass purchases bound to the event_id
            //   - event ticket purchases via tier → event
            //   - season pass reservations on the event
            // Each source maps the redemption fields to a unified CheckedIn / CheckedInAtUtc pair.
            // Cancelled rows are excluded so admins see the actual roll call.
            // Pass + ticket purchase rows track check-in via status='redeemed' — no
            // dedicated timestamp on those tables, so CheckedInAtUtc stays null for those
            // sources (we still get the boolean). Season pass reservations have a
            // checked_in_at column we can surface directly.
            const string sql = @"
                SELECT
                    p.id AS PurchaseId,
                    'pass' AS Source,
                    p.purchaser_name AS PurchaserName,
                    u.first_name AS FirstName,
                    u.last_name AS LastName,
                    p.purchaser_email AS PurchaserEmail,
                    u.phone AS PurchaserPhone,
                    pp.name AS ItemName,
                    NULL::text AS TierKind,
                    NULL::text AS RaceNumber,
                    u.race_number AS UserRaceNumber,
                    NULLIF(TRIM(BOTH ', ' FROM CONCAT_WS(', ', u.city, u.state)), '') AS Hometown,
                    p.quantity AS Quantity,
                    p.amount_cents AS AmountCents,
                    p.status AS Status,
                    (p.status = 'redeemed') AS CheckedIn,
                    p.redeemed_at_utc AS CheckedInAtUtc,
                    p.created_at AS CreatedAtUtc
                FROM pass_purchase p
                JOIN pass_product pp ON pp.id = p.product_id
                LEFT JOIN users u ON u.id = p.purchaser_user_id
                WHERE p.tenant_id = @tenantId
                  AND p.event_id = @eventId
                  AND p.status <> 'cancelled'

                UNION ALL

                SELECT
                    t.id AS PurchaseId,
                    'event_ticket' AS Source,
                    t.purchaser_name AS PurchaserName,
                    u.first_name AS FirstName,
                    u.last_name AS LastName,
                    t.purchaser_email AS PurchaserEmail,
                    u.phone AS PurchaserPhone,
                    tier.name AS ItemName,
                    tier.kind AS TierKind,
                    t.race_number AS RaceNumber,
                    u.race_number AS UserRaceNumber,
                    NULLIF(TRIM(BOTH ', ' FROM CONCAT_WS(', ', u.city, u.state)), '') AS Hometown,
                    1 AS Quantity,
                    t.amount_cents AS AmountCents,
                    t.status AS Status,
                    (t.status = 'redeemed') AS CheckedIn,
                    t.redeemed_at_utc AS CheckedInAtUtc,
                    t.created_at AS CreatedAtUtc
                FROM event_ticket_purchase t
                JOIN event_ticket_tier tier ON tier.id = t.tier_id
                LEFT JOIN users u ON u.id = t.purchaser_user_id
                WHERE t.tenant_id = @tenantId
                  AND tier.event_id = @eventId
                  AND t.status <> 'cancelled'

                UNION ALL

                SELECT
                    spr.id AS PurchaseId,
                    'season_pass' AS Source,
                    spp.purchaser_name AS PurchaserName,
                    u.first_name AS FirstName,
                    u.last_name AS LastName,
                    spp.purchaser_email AS PurchaserEmail,
                    u.phone AS PurchaserPhone,
                    sp.name AS ItemName,
                    NULL::text AS TierKind,
                    NULL::text AS RaceNumber,
                    u.race_number AS UserRaceNumber,
                    NULLIF(TRIM(BOTH ', ' FROM CONCAT_WS(', ', u.city, u.state)), '') AS Hometown,
                    1 AS Quantity,
                    0::bigint AS AmountCents,         -- the spend was on the season pass itself, not the reservation
                    spr.status AS Status,
                    (spr.checked_in_at IS NOT NULL) AS CheckedIn,
                    spr.checked_in_at AS CheckedInAtUtc,
                    spr.reserved_at AS CreatedAtUtc
                FROM season_pass_reservation spr
                JOIN season_pass_purchase spp ON spp.id = spr.season_pass_purchase_id
                JOIN season_pass_product sp ON sp.id = spp.product_id
                LEFT JOIN users u ON u.id = spp.purchaser_user_id
                WHERE spp.tenant_id = @tenantId
                  AND spr.event_id = @eventId
                  AND spr.status <> 'cancelled'

                ORDER BY CheckedIn DESC, PurchaserName";
            var rows = await _db.Query<EventRiderRow>(sql, new { tenantId, eventId });
            return rows.ToList();
        }

        public async Task<CheckInLookup?> LookupCheckInByToken(Guid tenantId, Guid token, DateTime fromUtc, DateTime toUtc)
        {
            // First identify the rider — the token may match any of three tables. We need
            // user_id (so we can find their other registrations), name/email/phone, the
            // photo if it's a season pass, and which kind matched (used by the UI).
            const string identifySql = @"
                SELECT 'pass' AS MatchedTokenKind, p.purchaser_user_id AS UserId,
                       p.purchaser_name AS PurchaserName, p.purchaser_email AS PurchaserEmail,
                       NULL::text AS PhotoDataUrl
                FROM pass_purchase p
                WHERE p.redemption_token = @token AND p.tenant_id = @tenantId
                LIMIT 1";
            var passRider = (await _db.Query<RiderIdentity>(identifySql, new { token, tenantId })).FirstOrDefault();

            RiderIdentity? rider = passRider;
            if (rider is null)
            {
                const string ticketSql = @"
                    SELECT 'event_ticket' AS MatchedTokenKind, t.purchaser_user_id AS UserId,
                           t.purchaser_name AS PurchaserName, t.purchaser_email AS PurchaserEmail,
                           NULL::text AS PhotoDataUrl
                    FROM event_ticket_purchase t
                    WHERE t.redemption_token = @token AND t.tenant_id = @tenantId
                    LIMIT 1";
                rider = (await _db.Query<RiderIdentity>(ticketSql, new { token, tenantId })).FirstOrDefault();
            }
            if (rider is null)
            {
                const string spSql = @"
                    SELECT 'season_pass' AS MatchedTokenKind, spp.purchaser_user_id AS UserId,
                           spp.purchaser_name AS PurchaserName, spp.purchaser_email AS PurchaserEmail,
                           spp.photo_data_url AS PhotoDataUrl
                    FROM season_pass_purchase spp
                    WHERE spp.redemption_token = @token AND spp.tenant_id = @tenantId
                    LIMIT 1";
                rider = (await _db.Query<RiderIdentity>(spSql, new { token, tenantId })).FirstOrDefault();
            }
            if (rider is null) return null;

            // Pull phone from the user record (purchase rows don't store it).
            string? phone = null;
            if (rider.UserId.HasValue)
            {
                const string phoneSql = "SELECT phone FROM users WHERE id = @userId LIMIT 1";
                phone = (await _db.Query<string?>(phoneSql, new { userId = rider.UserId })).FirstOrDefault();
            }

            var lookup = new CheckInLookup
            {
                UserId = rider.UserId,
                PurchaserName = rider.PurchaserName,
                PurchaserEmail = rider.PurchaserEmail,
                PurchaserPhone = phone,
                PhotoDataUrl = rider.PhotoDataUrl,
                MatchedTokenKind = rider.MatchedTokenKind,
            };

            // Gather all of this rider's registrations across the three sources, joined to
            // the parent event so we can return event title + start time. Pass + ticket
            // resolve via purchaser_user_id; season pass via the purchase → reservation chain.
            const string regsSql = @"
                SELECT
                    p.id AS Id,
                    'pass' AS Source,
                    e.id AS EventId,
                    e.title AS EventTitle,
                    e.starts_at AS EventStartsAtUtc,
                    e.ends_at AS EventEndsAtUtc,
                    pp.name AS ItemName,
                    p.status AS Status,
                    (p.status = 'redeemed') AS CheckedIn,
                    NULL::timestamptz AS CheckedInAtUtc,
                    p.redemption_token AS RedemptionToken
                FROM pass_purchase p
                JOIN pass_product pp ON pp.id = p.product_id
                JOIN event e ON e.id = p.event_id
                WHERE p.tenant_id = @tenantId
                  AND p.purchaser_user_id = @userId
                  AND p.event_id IS NOT NULL
                  AND p.status IN ('paid','redeemed')
                  AND e.starts_at >= @fromUtc
                  AND e.starts_at < @toUtc

                UNION ALL

                SELECT
                    t.id AS Id,
                    'event_ticket' AS Source,
                    e.id AS EventId,
                    e.title AS EventTitle,
                    e.starts_at AS EventStartsAtUtc,
                    e.ends_at AS EventEndsAtUtc,
                    tier.name AS ItemName,
                    t.status AS Status,
                    (t.status = 'redeemed') AS CheckedIn,
                    NULL::timestamptz AS CheckedInAtUtc,
                    t.redemption_token AS RedemptionToken
                FROM event_ticket_purchase t
                JOIN event_ticket_tier tier ON tier.id = t.tier_id
                JOIN event e ON e.id = tier.event_id
                WHERE t.tenant_id = @tenantId
                  AND t.purchaser_user_id = @userId
                  AND t.status IN ('paid','redeemed')
                  AND e.starts_at >= @fromUtc
                  AND e.starts_at < @toUtc

                UNION ALL

                SELECT
                    spr.id AS Id,
                    'season_pass' AS Source,
                    e.id AS EventId,
                    e.title AS EventTitle,
                    e.starts_at AS EventStartsAtUtc,
                    e.ends_at AS EventEndsAtUtc,
                    sp.name AS ItemName,
                    spr.status AS Status,
                    (spr.checked_in_at IS NOT NULL) AS CheckedIn,
                    spr.checked_in_at AS CheckedInAtUtc,
                    NULL::uuid AS RedemptionToken
                FROM season_pass_reservation spr
                JOIN season_pass_purchase spp ON spp.id = spr.season_pass_purchase_id
                JOIN season_pass_product sp ON sp.id = spp.product_id
                JOIN event e ON e.id = spr.event_id
                WHERE spp.tenant_id = @tenantId
                  AND spp.purchaser_user_id = @userId
                  AND spr.status <> 'cancelled'
                  AND e.starts_at >= @fromUtc
                  AND e.starts_at < @toUtc

                ORDER BY EventStartsAtUtc";
            var regs = rider.UserId.HasValue
                ? (await _db.Query<CheckInRegistration>(regsSql, new
                {
                    tenantId,
                    userId = rider.UserId,
                    fromUtc,
                    toUtc,
                })).ToList()
                : new List<CheckInRegistration>();

            // Split into today vs future at the caller's local-day boundary, which they
            // pass in as part of the [fromUtc, toUtc) window.
            var nowUtc = DateTime.UtcNow;
            var startOfTomorrow = fromUtc.AddDays(1);  // caller passes today's start as fromUtc
            foreach (var r in regs)
            {
                r.EventStartsAtUtc = DateTime.SpecifyKind(r.EventStartsAtUtc, DateTimeKind.Utc);
                r.EventEndsAtUtc = DateTime.SpecifyKind(r.EventEndsAtUtc, DateTimeKind.Utc);
                if (r.CheckedInAtUtc.HasValue)
                    r.CheckedInAtUtc = DateTime.SpecifyKind(r.CheckedInAtUtc.Value, DateTimeKind.Utc);
            }
            lookup.TodayRegistrations = regs.Where(r => r.EventStartsAtUtc < startOfTomorrow).ToList();
            lookup.FutureRegistrations = regs.Where(r => r.EventStartsAtUtc >= startOfTomorrow).ToList();

            return lookup;
        }

        private record RiderIdentity(string MatchedTokenKind, Guid? UserId,
            string PurchaserName, string PurchaserEmail, string? PhotoDataUrl);

        public async Task<List<DailyEventRow>> GetEventsInRange(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            // One row per event in the window, with registered/checked-in/revenue aggregates
            // summed across pass purchases + ticket purchases + season-pass reservations.
            const string sql = @"
                SELECT
                    e.id AS EventId,
                    e.title AS Title,
                    et.name AS EventTypeName,
                    e.starts_at AS StartsAtUtc,
                    e.ends_at AS EndsAtUtc,
                    e.all_day AS AllDay,
                    e.capacity AS Capacity,
                    e.status AS Status,
                    COALESCE(pass_agg.registered, 0)
                        + COALESCE(tk_agg.registered, 0)
                        + COALESCE(spr_agg.registered, 0) AS Registered,
                    COALESCE(pass_agg.checked_in, 0)
                        + COALESCE(tk_agg.checked_in, 0)
                        + COALESCE(spr_agg.checked_in, 0) AS CheckedIn,
                    COALESCE(pass_agg.revenue, 0)
                        + COALESCE(tk_agg.revenue, 0) AS RevenueCents
                FROM event e
                LEFT JOIN tenant_event_type et ON et.id = e.event_type_id
                LEFT JOIN (
                    SELECT event_id,
                           SUM(CASE WHEN status IN ('paid','redeemed') THEN quantity ELSE 0 END) AS registered,
                           SUM(CASE WHEN status = 'redeemed' THEN quantity ELSE 0 END) AS checked_in,
                           SUM(CASE WHEN status IN ('paid','redeemed') THEN amount_cents ELSE 0 END) AS revenue
                    FROM pass_purchase
                    WHERE tenant_id = @tenantId AND event_id IS NOT NULL
                    GROUP BY event_id
                ) pass_agg ON pass_agg.event_id = e.id
                LEFT JOIN (
                    SELECT tier.event_id,
                           SUM(CASE WHEN t.status IN ('paid','redeemed') THEN 1 ELSE 0 END) AS registered,
                           SUM(CASE WHEN t.status = 'redeemed' THEN 1 ELSE 0 END) AS checked_in,
                           SUM(CASE WHEN t.status IN ('paid','redeemed') THEN t.amount_cents ELSE 0 END) AS revenue
                    FROM event_ticket_purchase t
                    JOIN event_ticket_tier tier ON tier.id = t.tier_id
                    WHERE t.tenant_id = @tenantId
                    GROUP BY tier.event_id
                ) tk_agg ON tk_agg.event_id = e.id
                LEFT JOIN (
                    SELECT spr.event_id,
                           SUM(CASE WHEN spr.status IN ('reserved','checked_in') THEN 1 ELSE 0 END) AS registered,
                           SUM(CASE WHEN spr.checked_in_at IS NOT NULL THEN 1 ELSE 0 END) AS checked_in
                    FROM season_pass_reservation spr
                    JOIN season_pass_purchase spp ON spp.id = spr.season_pass_purchase_id
                    WHERE spp.tenant_id = @tenantId
                    GROUP BY spr.event_id
                ) spr_agg ON spr_agg.event_id = e.id
                WHERE e.tenant_id = @tenantId
                  AND e.starts_at >= @fromUtc
                  AND e.starts_at < @toUtc
                ORDER BY e.starts_at, e.title";
            var rows = await _db.Query<DailyEventRow>(sql, new { tenantId, fromUtc, toUtc });
            return rows.ToList();
        }
    }
}
