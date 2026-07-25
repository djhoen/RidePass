using Services.Helpers.Interfaces;
using Services.Repositories.Data.ReportData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class ReportsRepository : IReportsRepository
    {
        private readonly IDbHelper _db;

        public ReportsRepository(IDbHelper db) => _db = db;

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

        public async Task<AdmissionTaxTotals> GetAdmissionTaxTotals(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT
                    COALESCE(SUM(CASE WHEN status IN ('paid','redeemed') THEN tax_cents ELSE 0 END), 0) AS TaxCollectedCents,
                    COALESCE(SUM(CASE WHEN status IN ('paid','redeemed') AND tax_cents > 0 THEN amount_cents ELSE 0 END), 0) AS TaxableSalesCents,
                    COALESCE(SUM(CASE WHEN status IN ('paid','redeemed') AND tax_cents > 0 THEN 1 ELSE 0 END), 0)::int AS TaxedTicketCount,
                    COALESCE(SUM(CASE WHEN status = 'refunded' THEN tax_cents ELSE 0 END), 0) AS RefundedTaxCents
                FROM event_ticket_purchase
                WHERE tenant_id = @tenantId AND created_at >= @fromUtc AND created_at < @toUtc";
            var r = await _db.Query<AdmissionTaxTotals>(sql, new { tenantId, fromUtc, toUtc });
            return r.FirstOrDefault() ?? new AdmissionTaxTotals();
        }

        public async Task<List<RevenueByKindRow>> GetRevenueByKind(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            // Unified revenue from the ledger: every finalized sale (stripe, cash, voucher) writes
            // an entry_kind='sale' row, so this captures tickets, season passes, memberships, extras,
            // rentals, concessions, etc. in one place. Refunds are a separate entry_kind and excluded,
            // so this is GROSS revenue; the report surfaces refunds separately.
            const string sql = @"
                SELECT source_kind AS SourceKind,
                       COALESCE(SUM(gross_cents), 0)::bigint AS RevenueCents,
                       COUNT(*)::int AS SaleCount
                FROM tenant_ledger_entry
                WHERE tenant_id = @tenantId
                  AND entry_kind = 'sale'
                  AND occurred_at_utc >= @fromUtc AND occurred_at_utc < @toUtc
                GROUP BY source_kind
                ORDER BY RevenueCents DESC";
            return (await _db.Query<RevenueByKindRow>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<int> GetUniqueRiders(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            // Union distinct purchaser emails across passes and tickets within the range.
            const string sql = @"
                SELECT COUNT(DISTINCT email)::int FROM (
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
            // All-kinds gross revenue per local day, from the unified ledger so the chart sums to
            // the headline total. TicketsSold stays event-ticket-only (the count riders care about);
            // PassesSold is retired. Bucket on the tenant's local day, matching the report window.
            const string sql = @"
                SELECT to_char((occurred_at_utc AT TIME ZONE @timezone)::date, 'YYYY-MM-DD') AS Date,
                       COALESCE(SUM(gross_cents), 0)::bigint AS RevenueCents,
                       0 AS PassesSold,
                       COALESCE(SUM(CASE WHEN source_kind = 'event_ticket' THEN 1 ELSE 0 END), 0)::int AS TicketsSold
                FROM tenant_ledger_entry
                WHERE tenant_id = @tenantId
                  AND entry_kind = 'sale'
                  AND occurred_at_utc >= @fromUtc AND occurred_at_utc < @toUtc
                GROUP BY (occurred_at_utc AT TIME ZONE @timezone)::date
                ORDER BY (occurred_at_utc AT TIME ZONE @timezone)::date";
            var r = await _db.Query<DailyRevenuePoint>(sql, new { tenantId, fromUtc, toUtc, timezone });
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
                    (SELECT COALESCE(SUM(amount_cents), 0) FROM event_ticket_purchase
                        WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc)
                  AS RevenueCents,
                    0 AS PassesSold,
                    (SELECT COUNT(*) FROM event_ticket_purchase
                        WHERE status IN ('paid','redeemed') AND created_at >= @fromUtc AND created_at < @toUtc)::int
                  AS TicketsSold,
                    (SELECT COUNT(*) FROM event_ticket_purchase
                        WHERE status = 'refunded' AND created_at >= @fromUtc AND created_at < @toUtc)::int
                  AS RefundedCount,
                    (SELECT COUNT(*) FROM dispute
                        WHERE stripe_created_at >= @fromUtc AND stripe_created_at < @toUtc)::int
                  AS DisputedCount,
                    (SELECT COUNT(*) FROM tenant)::int AS TotalTenants,
                    (SELECT COUNT(DISTINCT tenant_id) FROM (
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
                       0 AS PassesSold,
                       COALESCE(tk.tickets_sold, 0)::int AS TicketsSold,
                       COALESCE(tk.revenue, 0)::bigint AS RevenueCents,
                       COALESCE(tk.refunded, 0)::int AS RefundedCount,
                       COALESCE(disp.disputed, 0)::int AS DisputedCount
                FROM tenant t
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
                    t.id AS PurchaseId,
                    'event_ticket' AS Source,
                    t.purchaser_name AS PurchaserName,
                    u.first_name AS FirstName,
                    u.last_name AS LastName,
                    t.purchaser_email AS PurchaserEmail,
                    u.phone AS PurchaserPhone,
                    tier.name AS ItemName,
                    tier.kind AS TierKind,
                    tier.audience AS TierAudience,
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
                    NULL::text AS TierAudience,
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

        // Shared body for the Rider/Spectator Reports and the rider drill-in: tickets (+
        // season-pass reservations for riders) mapped to one row shape (event, rider identity,
        // wristband, per-purchase waiver flag). The caller supplies the event-window predicate,
        // audience filter, and identity filter. Spectator tiers are spectator_pass kind or
        // gate_fee rows sold to the spectator audience; everything else is a rider.
        private const string SpectatorTierExpr = "(tier.kind = 'spectator_pass' OR tier.audience = 'spectator')";

        // A ticket's purchase bucket. A gate fee redeemed against a season pass reports as that
        // pass's type, not as a day ticket: the rider walked in on their pass, which is what the
        // filter is asked to answer. Everything else falls back to the tier's own kind.
        private const string TicketPurchaseTypeExpr = @"
                       CASE
                           WHEN tier.kind = 'race_entry' THEN 'race_entry'
                           WHEN tier.kind = 'spectator_pass' OR tier.audience = 'spectator' THEN 'spectator_pass'
                           WHEN aspp.id IS NOT NULL THEN
                               CASE asp.kind
                                   WHEN 'credits' THEN 'season_pass_credits'
                                   WHEN 'days_of_week' THEN 'season_pass_days'
                                   ELSE 'season_pass_unlimited'
                               END
                           ELSE 'day_ticket'
                       END";

        // Same buckets for a season-pass reservation, read off the pass product itself.
        private const string SeasonPassPurchaseTypeExpr = @"
                       CASE sp.kind
                           WHEN 'credits' THEN 'season_pass_credits'
                           WHEN 'days_of_week' THEN 'season_pass_days'
                           ELSE 'season_pass_unlimited'
                       END";

        private const string RiderTicketBranch = @"
                SELECT t.id AS PurchaseId,
                       'ticket' AS Source,
                       e.id AS EventId, e.title AS EventTitle, e.starts_at AS EventStartsAtUtc,
                       COALESCE(NULLIF(TRIM(CONCAT_WS(' ', t.rider_first_name, t.rider_last_name)), ''),
                                t.purchaser_name, '(unknown)') AS RiderName,
                       t.purchaser_email AS Email,
                       t.purchaser_user_id AS UserId,
                       tier.name AS ItemName,
                       (t.status = 'redeemed') AS CheckedIn,
                       t.redeemed_at_utc AS CheckedInAtUtc,
                       wb.code AS WristbandCode,
                       (t.waiver_signature_id IS NOT NULL OR t.waiver_signed_at IS NOT NULL
                            OR t.waiver_signature_data_url IS NOT NULL) AS SignedForThis,
{TICKET_PURCHASE_TYPE} AS PurchaseType,
                       tet.name AS EventTypeName, tet.code AS EventTypeCode,
                       COALESCE(t.registration_complete, false) AS RegistrationComplete,
                       t.rider_birthdate AS RiderBirthdate
                FROM event_ticket_purchase t
                JOIN event_ticket_tier tier ON tier.id = t.tier_id
                JOIN event e ON e.id = tier.event_id
                LEFT JOIN tenant_event_type tet ON tet.id = e.event_type_id
                LEFT JOIN event_wristband wb ON wb.ticket_id = t.id AND wb.tenant_id = t.tenant_id
                LEFT JOIN season_pass_purchase aspp ON aspp.id = t.applied_season_pass_purchase_id
                                                   AND aspp.tenant_id = t.tenant_id
                LEFT JOIN season_pass_product asp ON asp.id = aspp.product_id
                WHERE t.tenant_id = @tenantId
                  AND t.status <> 'cancelled'
                  AND {AUDIENCE_FILTER}
                  AND {EVENT_WINDOW}";

        // The tenant's IANA zone, defaulted, for converting between walk-up calendar dates and the
        // UTC instants the rest of the report speaks. Matches the fallback NextOrderNumber uses.
        private const string TenantZoneExpr =
            "COALESCE(NULLIF((SELECT t.timezone FROM tenant t WHERE t.id = @tenantId), ''), 'UTC')";

        // A season-pass admission anchors to EITHER an event or a tenant-local calendar date
        // (Script0236: walk-up tracks can open the lift on a day with nothing on the calendar).
        // The event join is therefore a LEFT join: an INNER join silently drops every walk-up row,
        // and walk-up is the DEFAULT admission type, so that would hide the common case.
        //
        // EventStartsAtUtc for a walk-up is its check_in_date read as MIDNIGHT IN THE TENANT'S ZONE,
        // not midnight UTC. Casting the date straight to timestamptz would land it on the previous
        // evening for any negative-offset track and the row would render under the wrong day.
        private const string RiderSeasonPassBranch = @"
                SELECT spr.id, 'season_pass',
                       e.id, e.title,
                       COALESCE(e.starts_at, (spr.check_in_date::timestamp AT TIME ZONE " + TenantZoneExpr + @")),
                       COALESCE(NULLIF(TRIM(CONCAT_WS(' ', spp.holder_first_name, spp.holder_last_name)), ''),
                                spp.purchaser_name, '(unknown)'),
                       spp.purchaser_email,
                       spp.purchaser_user_id,
                       sp.name,
                       (spr.checked_in_at IS NOT NULL),
                       spr.checked_in_at,
                       NULL::text,
                       false,
{SEASON_PASS_PURCHASE_TYPE},
                       tet.name, tet.code,
                       true,
                       spp.holder_birthdate
                FROM season_pass_reservation spr
                JOIN season_pass_purchase spp ON spp.id = spr.season_pass_purchase_id
                JOIN season_pass_product sp ON sp.id = spp.product_id
                LEFT JOIN event e ON e.id = spr.event_id AND e.tenant_id = spp.tenant_id
                LEFT JOIN tenant_event_type tet ON tet.id = e.event_type_id
                WHERE spp.tenant_id = @tenantId
                  AND spr.status <> 'cancelled'
                  AND {SEASON_PASS_WINDOW}";

        // Composes the CTE for one audience: spectators are ticket-only (a season pass
        // reservation is always a rider), riders get both branches. The active-waiver set is
        // hoisted alongside so the per-row coverage check below doesn't re-derive it.
        private static string RiderRowsCteFor(string audience) =>
            "WITH rows AS (" + (audience switch
            {
                "spectator" => RiderTicketBranch.Replace("{AUDIENCE_FILTER}", SpectatorTierExpr),
                "all" => RiderTicketBranch.Replace("{AUDIENCE_FILTER}", "true")
                    + "\n                UNION ALL\n" + RiderSeasonPassBranch,
                _ => RiderTicketBranch.Replace("{AUDIENCE_FILTER}", $"NOT {SpectatorTierExpr}")
                    + "\n                UNION ALL\n" + RiderSeasonPassBranch,
            })
                .Replace("{TICKET_PURCHASE_TYPE}", TicketPurchaseTypeExpr)
                .Replace("{SEASON_PASS_PURCHASE_TYPE}", SeasonPassPurchaseTypeExpr)
            + "\n            ),\n" + ActiveWaiverCte;

        // The tenant's currently-active waivers, resolved once per query. Materialized on purpose:
        // it's a handful of rows probed by every coverage branch below, so re-planning it inline
        // for each one buys nothing.
        private const string ActiveWaiverCte = @"
            active_waiver AS MATERIALIZED (
                SELECT tw.id
                FROM tenant_waiver tw
                WHERE tw.tenant_id = @tenantId
                  AND tw.is_active AND (tw.expires_at IS NULL OR tw.expires_at > now())
            )";

        // Account/email-level waiver coverage on a currently-active waiver, same matching the
        // Compliance screen uses (account id first, else email against signer or account email).
        //
        // PERFORMANCE: this runs once per report row, so each match path has to be index-driven.
        // It used to be ONE EXISTS that ORed all three paths together inside a single scan of
        // rider_waiver_signature. No index can serve an OR across three different columns, so
        // Postgres fell back to a full scan of the tenant's signatures for EVERY row that wasn't
        // already signed-for-this-ticket, plus a full scan of users inside it. Measured on stage
        // (Highland, 32k signatures) that was 8.3 s for a single day of riders, and it degrades
        // linearly with both signature history and rows on screen.
        //
        // Split into one EXISTS per path, each hitting an existing index
        // (uk_rider_waiver_once_user, idx_rider_waiver_sig_signer_email, users_pkey), the same
        // query returns the same rows in 16 ms. Keep them separate: re-merging them into one
        // OR'd EXISTS silently restores the full scan.
        private const string RiderWaiverCoverageExpr = @"
            (r.SignedForThis
             -- Signed under their own account.
             OR (r.UserId IS NOT NULL AND EXISTS (
                    SELECT 1 FROM rider_waiver_signature ws
                    WHERE ws.tenant_id = @tenantId AND ws.user_id = r.UserId
                      AND ws.waiver_id IN (SELECT id FROM active_waiver)))
             -- Signed as a guest under this row's email.
             OR (r.Email IS NOT NULL AND r.Email <> '' AND EXISTS (
                    SELECT 1 FROM rider_waiver_signature ws
                    WHERE ws.tenant_id = @tenantId
                      AND lower(ws.signer_email) = lower(r.Email)
                      AND ws.waiver_id IN (SELECT id FROM active_waiver)))
             -- Signed under an account whose email matches this row's email (the buyer holds an
             -- account but the ticket carries only their address).
             OR (r.Email IS NOT NULL AND r.Email <> '' AND EXISTS (
                    SELECT 1 FROM users uu
                    JOIN rider_waiver_signature ws ON ws.user_id = uu.id
                    WHERE lower(uu.email) = lower(r.Email)
                      AND ws.tenant_id = @tenantId
                      AND ws.waiver_id IN (SELECT id FROM active_waiver))))";

        // Date-range window for season-pass admissions. Event-anchored rows keep comparing against
        // the event's UTC start; walk-up rows compare their tenant-LOCAL calendar date against the
        // window's bounds converted into that same local calendar, so a Denver track's "July 4"
        // report can't pull in a walk-up stamped July 3 or miss one stamped July 4.
        private const string RangeSeasonPassWindow = @"(
                      (spr.event_id IS NOT NULL
                          AND e.tenant_id = @tenantId AND e.starts_at >= @fromUtc AND e.starts_at < @toUtc)
                   OR (spr.event_id IS NULL
                          AND spr.check_in_date >= (@fromUtc AT TIME ZONE " + TenantZoneExpr + @")::date
                          AND spr.check_in_date <  (@toUtc   AT TIME ZONE " + TenantZoneExpr + @")::date)
                  )";

        // Same split for the rider drill-in's rolling last-365-days window.
        private const string RegistrationsSeasonPassWindow = @"(
                      (spr.event_id IS NOT NULL
                          AND e.tenant_id = @tenantId AND e.starts_at >= now() - INTERVAL '365 days')
                   OR (spr.event_id IS NULL
                          AND spr.check_in_date >= ((now() - INTERVAL '365 days') AT TIME ZONE " + TenantZoneExpr + @")::date)
                  )";

        public async Task<List<RiderReportRow>> GetRidersByRange(Guid tenantId, DateTime fromUtc, DateTime toUtc,
            string? search, int cap, string audience = "rider",
            IReadOnlyList<string>? purchaseTypes = null, IReadOnlyList<string>? eventTypeCodes = null)
        {
            var searchSql = string.IsNullOrWhiteSpace(search) ? "" : @"
                  AND (lower(r.RiderName) LIKE @search
                       OR lower(COALESCE(r.Email, '')) LIKE @search
                       OR lower(COALESCE(r.WristbandCode, '')) LIKE @search)";
            // Both filters are applied against the CTE's derived columns, so they narrow the set
            // BEFORE the row cap: a filtered report can reach rows a capped unfiltered one hid.
            var purchaseTypeSql = purchaseTypes is { Count: > 0 }
                ? "\n                  AND r.PurchaseType = ANY(@purchaseTypes)" : "";
            var eventTypeSql = eventTypeCodes is { Count: > 0 }
                ? "\n                  AND r.EventTypeCode = ANY(@eventTypeCodes)" : "";
            var sql = RiderRowsCteFor(audience)
                .Replace("{EVENT_WINDOW}", "e.tenant_id = @tenantId AND e.starts_at >= @fromUtc AND e.starts_at < @toUtc")
                .Replace("{SEASON_PASS_WINDOW}", RangeSeasonPassWindow) + $@"
                SELECT r.*, {RiderWaiverCoverageExpr} AS WaiverSigned
                FROM rows r
                WHERE true{searchSql}{purchaseTypeSql}{eventTypeSql}
                ORDER BY r.EventStartsAtUtc, r.RiderName
                LIMIT @cap";
            var rows = await _db.Query<RiderReportRow>(sql, new
            {
                tenantId, fromUtc, toUtc, cap,
                search = $"%{search?.Trim().ToLowerInvariant()}%",
                purchaseTypes = purchaseTypes?.ToArray(),
                eventTypeCodes = eventTypeCodes?.ToArray(),
            });
            return rows.ToList();
        }

        public async Task<List<RiderReportRow>> GetRiderRegistrations(Guid tenantId, Guid? userId, string? email)
        {
            var sql = RiderRowsCteFor("all")
                .Replace("{EVENT_WINDOW}", "e.tenant_id = @tenantId AND e.starts_at >= now() - INTERVAL '365 days'")
                .Replace("{SEASON_PASS_WINDOW}", RegistrationsSeasonPassWindow) + $@"
                SELECT r.*, {RiderWaiverCoverageExpr} AS WaiverSigned
                FROM rows r
                WHERE ((@userId::uuid IS NOT NULL AND r.UserId = @userId)
                    OR (@email IS NOT NULL AND lower(COALESCE(r.Email, '')) = lower(@email)))
                ORDER BY r.EventStartsAtUtc DESC
                LIMIT 100";
            var rows = await _db.Query<RiderReportRow>(sql, new { tenantId, userId, email });
            return rows.ToList();
        }

        public async Task<List<RiderWaiverRow>> GetRiderWaivers(Guid tenantId, Guid? userId, string? email)
        {
            // The signature image is deliberately NOT selected: it's a base64 data URL per row and a
            // regular at a busy park racks up dozens. Only the flag ships; GetWaiverSignatureImage
            // fetches the one the admin actually opens.
            const string sql = @"
                SELECT s.id,
                       w.name AS WaiverName,
                       w.version AS WaiverVersion,
                       s.signed_at AS SignedAtUtc,
                       s.signed_by_parent AS SignedByParent,
                       s.parent_name AS ParentName,
                       s.signer_name AS SignerName,
                       (s.signature_data_url IS NOT NULL) AS HasSignatureImage,
                       (w.is_active AND (w.expires_at IS NULL OR w.expires_at > now())) AS WaiverIsCurrent
                FROM rider_waiver_signature s
                JOIN tenant_waiver w ON w.id = s.waiver_id
                WHERE s.tenant_id = @tenantId
                  AND ((@userId::uuid IS NOT NULL AND s.user_id = @userId)
                    OR (@email IS NOT NULL AND
                         (lower(COALESCE(s.signer_email, '')) = lower(@email)
                          OR s.user_id IN (SELECT uu.id FROM users uu WHERE lower(uu.email) = lower(@email)))))
                ORDER BY s.signed_at DESC
                LIMIT 100";
            var rows = await _db.Query<RiderWaiverRow>(sql, new { tenantId, userId, email });
            return rows.ToList();
        }

        /// <summary>
        /// One signature's image, tenant-scoped by the signature id. Separate from the drill-in so
        /// the payload only carries an image when an admin asks to see that specific one.
        /// </summary>
        public async Task<string?> GetWaiverSignatureImage(Guid tenantId, Guid signatureId)
        {
            const string sql = @"
                SELECT signature_data_url
                FROM rider_waiver_signature
                WHERE id = @signatureId AND tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<string?>(sql, new { tenantId, signatureId })).FirstOrDefault();
        }

        /// <summary>
        /// Identity + lifetime totals for the drill-in header. Identity comes from the account when
        /// there is one; guests (no account) fall back to the details captured on their most recent
        /// purchase, which is the only place a guest's phone/emergency contact is ever recorded.
        /// </summary>
        public async Task<RiderProfileRow?> GetRiderProfile(Guid tenantId, Guid? userId, string? email)
        {
            const string sql = @"
                WITH matched_tickets AS (
                    SELECT t.*
                    FROM event_ticket_purchase t
                    WHERE t.tenant_id = @tenantId
                      AND t.status <> 'cancelled'
                      AND ((@userId::uuid IS NOT NULL AND t.purchaser_user_id = @userId)
                        OR (@email IS NOT NULL AND lower(COALESCE(t.purchaser_email, '')) = lower(@email)))
                ),
                -- Most recent purchase that actually captured each detail, so an older ticket can
                -- still supply a phone the latest one left blank.
                latest AS (
                    SELECT
                        (SELECT bike FROM matched_tickets WHERE bike IS NOT NULL
                          ORDER BY created_at DESC LIMIT 1) AS Bike,
                        (SELECT emergency_contact_name FROM matched_tickets WHERE emergency_contact_name IS NOT NULL
                          ORDER BY created_at DESC LIMIT 1) AS EmergencyContactName,
                        (SELECT emergency_contact_phone FROM matched_tickets WHERE emergency_contact_phone IS NOT NULL
                          ORDER BY created_at DESC LIMIT 1) AS EmergencyContactPhone,
                        (SELECT parent_guardian_name FROM matched_tickets WHERE parent_guardian_name IS NOT NULL
                          ORDER BY created_at DESC LIMIT 1) AS ParentGuardianName,
                        (SELECT rider_birthdate FROM matched_tickets WHERE rider_birthdate IS NOT NULL
                          ORDER BY created_at DESC LIMIT 1) AS TicketBirthdate,
                        (SELECT race_number FROM matched_tickets WHERE race_number IS NOT NULL
                          ORDER BY created_at DESC LIMIT 1) AS TicketRaceNumber
                ),
                -- Season-pass reservations are entries too, so a pass holder's counts can't read as
                -- zero next to a Registered-for list full of rows. They carry no spend: the money
                -- was on the pass itself, not the reservation.
                matched_entries AS (
                    SELECT (status = 'redeemed') AS attended, amount_cents,
                           created_at, COALESCE(redeemed_at_utc, created_at) AS last_seen
                    FROM matched_tickets
                    UNION ALL
                    SELECT (spr.checked_in_at IS NOT NULL), 0,
                           spr.reserved_at, COALESCE(spr.checked_in_at, spr.reserved_at)
                    FROM season_pass_reservation spr
                    JOIN season_pass_purchase spp ON spp.id = spr.season_pass_purchase_id
                    WHERE spp.tenant_id = @tenantId
                      AND spr.status <> 'cancelled'
                      AND ((@userId::uuid IS NOT NULL AND spp.purchaser_user_id = @userId)
                        OR (@email IS NOT NULL AND lower(COALESCE(spp.purchaser_email, '')) = lower(@email)))
                ),
                totals AS (
                    SELECT COUNT(*)::int AS TotalRegistrations,
                           COUNT(*) FILTER (WHERE attended)::int AS TotalCheckedIn,
                           COALESCE(SUM(amount_cents), 0)::bigint AS TotalSpentCents,
                           MIN(created_at) AS FirstVisitUtc,
                           MAX(last_seen) AS LastVisitUtc
                    FROM matched_entries
                )
                SELECT u.id AS UserId,
                       COALESCE(u.email, @email) AS Email,
                       u.phone AS Phone,
                       NULLIF(TRIM(BOTH ', ' FROM CONCAT_WS(', ', u.city, u.state)), '') AS Hometown,
                       COALESCE(u.race_number, latest.TicketRaceNumber) AS RaceNumber,
                       COALESCE(u.birthdate, latest.TicketBirthdate) AS Birthdate,
                       u.created_at AS MemberSinceUtc,
                       latest.Bike, latest.EmergencyContactName, latest.EmergencyContactPhone,
                       latest.ParentGuardianName,
                       totals.TotalRegistrations, totals.TotalCheckedIn, totals.TotalSpentCents,
                       totals.FirstVisitUtc, totals.LastVisitUtc
                FROM totals
                CROSS JOIN latest
                LEFT JOIN users u ON (@userId::uuid IS NOT NULL AND u.id = @userId)
                                  OR (@userId::uuid IS NULL AND @email IS NOT NULL AND lower(u.email) = lower(@email))
                -- An email can resolve to more than one account (a rider's global account and a
                -- tenant-scoped staff row). Oldest wins so the header doesn't flip between loads.
                ORDER BY u.created_at
                LIMIT 1";
            return (await _db.Query<RiderProfileRow>(sql, new { tenantId, userId, email })).FirstOrDefault();
        }

        // "Who has signed" report for one event: each event ticket's attendee and their waiver signing
        // status, read from the normalized rider_waiver_signature store via the ticket's link, so
        // counter and online sales report uniformly. Tenant- and event-scoped.
        public async Task<List<EventWaiverSignatureRow>> GetEventWaiverSignatures(Guid tenantId, Guid eventId)
        {
            const string sql = @"
                SELECT
                    p.id AS PurchaseId,
                    COALESCE(NULLIF(TRIM(CONCAT_WS(' ', p.rider_first_name, p.rider_last_name)), ''), p.purchaser_name) AS AttendeeName,
                    CASE WHEN tier.kind = 'race_entry' OR (tier.kind = 'gate_fee' AND tier.audience = 'rider')
                         THEN 'rider' ELSE 'spectator' END AS Audience,
                    tier.name AS TierName,
                    p.race_number AS RaceNumber,
                    p.status AS Status,
                    p.registration_complete AS RegistrationComplete,
                    CASE WHEN tier.kind = 'race_entry' OR (tier.kind = 'gate_fee' AND tier.audience = 'rider')
                         THEN e.requires_rider_waiver ELSE e.requires_spectator_waiver END AS WaiverRequired,
                    (p.waiver_signature_id IS NOT NULL OR p.waiver_signed_at IS NOT NULL OR p.waiver_signature_data_url IS NOT NULL) AS WaiverSigned,
                    COALESCE(sig.signed_at, p.waiver_signed_at) AS SignedAtUtc,
                    COALESCE(sig.signed_by_parent, false) AS SignedByParent,
                    COALESCE(sig.parent_name, p.parent_guardian_name) AS ParentGuardianName,
                    sig.signer_name AS SignerName
                FROM event_ticket_purchase p
                JOIN event_ticket_tier tier ON tier.id = p.tier_id
                JOIN event e ON e.id = tier.event_id
                LEFT JOIN rider_waiver_signature sig
                       ON sig.id = p.waiver_signature_id AND sig.tenant_id = p.tenant_id
                WHERE p.tenant_id = @tenantId
                  AND tier.event_id = @eventId
                  AND p.status <> 'cancelled'
                ORDER BY Audience, tier.name, AttendeeName";
            var rows = await _db.Query<EventWaiverSignatureRow>(sql, new { tenantId, eventId });
            return rows.ToList();
        }

        public async Task<CheckInLookup?> LookupCheckInByToken(Guid tenantId, Guid token, DateTime fromUtc, DateTime toUtc)
        {
            // First identify the rider — the token may match any of three tables. We need
            // user_id (so we can find their other registrations), name/email/phone, the
            // photo if it's a season pass, and which kind matched (used by the UI).
            const string ticketSql = @"
                SELECT 'event_ticket' AS MatchedTokenKind, t.purchaser_user_id AS UserId,
                       t.purchaser_name AS PurchaserName, t.purchaser_email AS PurchaserEmail,
                       NULL::text AS PhotoDataUrl
                FROM event_ticket_purchase t
                WHERE t.redemption_token = @token AND t.tenant_id = @tenantId
                LIMIT 1";
            RiderIdentity? rider = (await _db.Query<RiderIdentity>(ticketSql, new { token, tenantId })).FirstOrDefault();
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
                    COALESCE(e.title, 'Walk-up admission') AS EventTitle,
                    COALESCE(e.starts_at, (spr.check_in_date::timestamp AT TIME ZONE " + TenantZoneExpr + @")) AS EventStartsAtUtc,
                    COALESCE(e.ends_at,   (spr.check_in_date::timestamp AT TIME ZONE " + TenantZoneExpr + @")) AS EventEndsAtUtc,
                    sp.name AS ItemName,
                    spr.status AS Status,
                    (spr.checked_in_at IS NOT NULL) AS CheckedIn,
                    spr.checked_in_at AS CheckedInAtUtc,
                    NULL::uuid AS RedemptionToken
                FROM season_pass_reservation spr
                JOIN season_pass_purchase spp ON spp.id = spr.season_pass_purchase_id
                JOIN season_pass_product sp ON sp.id = spp.product_id
                LEFT JOIN event e ON e.id = spr.event_id AND e.tenant_id = spp.tenant_id
                WHERE spp.tenant_id = @tenantId
                  AND spp.purchaser_user_id = @userId
                  AND spr.status <> 'cancelled'
                  AND (
                        (e.starts_at >= @fromUtc AND e.starts_at < @toUtc)
                        OR (spr.event_id IS NULL AND spr.check_in_date IS NOT NULL
                            AND spr.check_in_date >= (@fromUtc AT TIME ZONE " + TenantZoneExpr + @")::date
                            AND spr.check_in_date <  (@toUtc   AT TIME ZONE " + TenantZoneExpr + @")::date)
                      )

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
                    COALESCE(tk_agg.registered, 0)
                        + COALESCE(spr_agg.registered, 0) AS Registered,
                    COALESCE(tk_agg.checked_in, 0)
                        + COALESCE(spr_agg.checked_in, 0) AS CheckedIn,
                    COALESCE(tk_agg.revenue, 0) AS RevenueCents
                FROM event e
                LEFT JOIN tenant_event_type et ON et.id = e.event_type_id
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
