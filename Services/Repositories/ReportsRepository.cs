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
