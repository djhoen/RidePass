using Dapper;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.LessonData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class LessonChangeRepository : ILessonChangeRepository
    {
        private readonly IDbHelper _db;

        public LessonChangeRepository(IDbHelper db) => _db = db;

        public async Task<LessonTicketContext?> GetTicket(Guid tenantId, Guid ticketId)
        {
            const string sql = @"
                SELECT p.id AS TicketId, p.status, p.tier_id AS TierId, t.name AS TierName, t.kind AS TierKind,
                       t.audience AS TierAudience, e.id AS EventId, e.title AS EventTitle, e.event_type_id AS EventTypeId,
                       tet.code AS EventTypeCode, e.status AS EventStatus,
                       COALESCE(t.starts_at, e.starts_at) AS SessionStartsAt,
                       e.weather_cancelled_at AS WeatherCancelledAt, e.weather_cancelled_by_user_id AS WeatherCancelledByUserId,
                       p.purchaser_user_id AS PurchaserUserId, p.purchaser_email AS PurchaserEmail,
                       p.purchaser_name AS PurchaserName,
                       NULLIF(TRIM(COALESCE(p.rider_first_name, '') || ' ' || COALESCE(p.rider_last_name, '')), '') AS RiderName,
                       p.redemption_token AS RedemptionToken, p.amount_cents AS AmountCents, p.payment_method AS PaymentMethod
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id AND e.tenant_id = p.tenant_id
                JOIN tenant_event_type tet ON tet.id = e.event_type_id
                WHERE p.id = @ticketId AND p.tenant_id = @tenantId";
            return (await _db.Query<LessonTicketContext>(sql, new { tenantId, ticketId })).FirstOrDefault();
        }

        public async Task<List<LessonTicketFlag>> LessonFlags(Guid tenantId, IReadOnlyList<Guid> ticketIds)
        {
            const string sql = @"
                SELECT p.id AS TicketId, (e.weather_cancelled_at IS NOT NULL) AS WeatherCancelled
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id AND e.tenant_id = p.tenant_id
                JOIN tenant_event_type tet ON tet.id = e.event_type_id
                WHERE p.tenant_id = @tenantId AND p.id = ANY(@ids) AND tet.code = 'lesson'";
            return (await _db.Query<LessonTicketFlag>(sql, new { tenantId, ids = ticketIds.ToArray() })).ToList();
        }

        // Spots left = the tightest of the group's inventory, its coach's cap and the event's capacity;
        // NULL when none applies. Counted like checkout: pending, paid and redeemed rows hold a spot.
        // Needs aliases t (tier), e (event) and i (the tier's instructor, LEFT JOIN).
        private const string SpotsLeftSql = @"
            LEAST(
                CASE WHEN t.inventory IS NULL AND i.max_students_per_session IS NULL THEN NULL
                     ELSE LEAST(COALESCE(t.inventory, 2147483647), COALESCE(i.max_students_per_session, 2147483647))
                          - (SELECT COUNT(*) FROM event_ticket_purchase s
                             WHERE s.tier_id = t.id AND s.status IN ('pending', 'paid', 'redeemed')) END,
                CASE WHEN e.capacity IS NULL OR t.audience <> 'rider' THEN NULL
                     ELSE e.capacity - (SELECT COUNT(*) FROM event_ticket_purchase s
                                        JOIN event_ticket_tier st ON st.id = s.tier_id
                                        WHERE st.event_id = e.id AND st.audience = 'rider'
                                          AND s.status IN ('pending', 'paid', 'redeemed')) END)::int";

        public async Task<List<LessonRebookOption>> ListOptions(Guid tenantId, Guid eventTypeId, Guid excludeEventId,
            string tierKind, DateTime notBeforeUtc)
        {
            var sql = $@"
                SELECT * FROM (
                    SELECT e.id AS EventId, e.title AS EventTitle, t.id AS TierId, t.name AS TierName,
                           COALESCE(t.starts_at, e.starts_at) AS StartsAt, COALESCE(t.ends_at, e.ends_at) AS EndsAt,
                           {SpotsLeftSql} AS SpotsLeft
                    FROM event e
                    JOIN event_ticket_tier t ON t.event_id = e.id
                    LEFT JOIN instructor i ON i.id = t.instructor_id AND i.tenant_id = e.tenant_id
                    WHERE e.tenant_id = @tenantId AND e.event_type_id = @eventTypeId AND e.id <> @excludeEventId
                      AND e.status = 'scheduled' AND e.weather_cancelled_at IS NULL
                      AND t.is_active AND t.kind = @tierKind
                      AND COALESCE(t.starts_at, e.starts_at) > @notBeforeUtc
                ) o
                WHERE o.SpotsLeft IS NULL OR o.SpotsLeft > 0
                ORDER BY o.StartsAt, lower(o.TierName)
                LIMIT 300";
            return (await _db.Query<LessonRebookOption>(sql, new { tenantId, eventTypeId, excludeEventId, tierKind, notBeforeUtc })).ToList();
        }

        public async Task<bool> HasSessionRental(Guid tenantId, Guid eventId, Guid? purchaserUserId, string purchaserEmail)
        {
            const string sql = @"
                SELECT EXISTS (
                    SELECT 1 FROM shop_rental r
                    WHERE r.tenant_id = @tenantId AND r.event_id = @eventId
                      AND r.status IN ('pending', 'paid', 'out')
                      AND ((@purchaserUserId::uuid IS NOT NULL AND r.renter_user_id = @purchaserUserId)
                           OR lower(r.renter_email) = lower(@purchaserEmail)))";
            return (await _db.Query<bool>(sql, new { tenantId, eventId, purchaserUserId, purchaserEmail })).FirstOrDefault();
        }

        public async Task<string?> MoveTicket(Guid tenantId, Guid ticketId, Guid fromTierId, Guid toTierId, string source, Guid? byUserId)
        {
            return await _db.WithTransaction<string?>(async (conn, tx) =>
            {
                var found = (await conn.QueryAsync<SpotsRow>($@"
                    SELECT t.id AS TierId, {SpotsLeftSql} AS SpotsLeft
                    FROM event_ticket_tier t
                    JOIN event e ON e.id = t.event_id
                    LEFT JOIN instructor i ON i.id = t.instructor_id AND i.tenant_id = e.tenant_id
                    WHERE t.id = @toTierId AND e.tenant_id = @tenantId AND e.status = 'scheduled'
                      AND e.weather_cancelled_at IS NULL AND t.is_active",
                    new { toTierId, tenantId }, tx)).FirstOrDefault();
                if (found is null) return "That session isn't available any more.";
                if (found.SpotsLeft is int n && n <= 0) return "That session just filled up. Pick another date.";

                var moved = await conn.ExecuteAsync(@"
                    UPDATE event_ticket_purchase SET tier_id = @toTierId, updated_at = now()
                    WHERE id = @ticketId AND tenant_id = @tenantId AND tier_id = @fromTierId AND status = 'paid'",
                    new { ticketId, tenantId, fromTierId, toTierId }, tx);
                if (moved == 0) return "This booking changed while you were looking. Reload and try again.";

                await conn.ExecuteAsync(@"
                    INSERT INTO lesson_rebook (tenant_id, ticket_id, from_tier_id, to_tier_id, source, by_user_id)
                    VALUES (@tenantId, @ticketId, @fromTierId, @toTierId, @source, @byUserId)",
                    new { tenantId, ticketId, fromTierId, toTierId, source, byUserId }, tx);
                return null;
            });
        }

        private sealed class SpotsRow
        {
            public Guid TierId { get; set; }
            public int? SpotsLeft { get; set; }
        }

        public async Task<List<LessonSessionRow>> ListSessions(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT e.id AS EventId, e.title, e.starts_at AS StartsAt, e.ends_at AS EndsAt, e.status,
                       e.weather_cancelled_at AS WeatherCancelledAt,
                       (SELECT COUNT(*) FROM event_ticket_purchase p JOIN event_ticket_tier t ON t.id = p.tier_id
                        WHERE t.event_id = e.id AND p.tenant_id = e.tenant_id AND p.status IN ('paid', 'redeemed'))::int AS Booked
                FROM event e
                JOIN tenant_event_type tet ON tet.id = e.event_type_id
                WHERE e.tenant_id = @tenantId AND tet.code = 'lesson'
                  AND e.ends_at >= @fromUtc AND e.starts_at < @toUtc
                ORDER BY e.starts_at, lower(e.title)
                LIMIT 300";
            return (await _db.Query<LessonSessionRow>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<List<LessonBookingRow>> ListBookings(Guid tenantId, Guid eventId)
        {
            const string sql = @"
                SELECT p.id AS TicketId, p.status, t.name AS TierName,
                       COALESCE(NULLIF(TRIM(COALESCE(p.rider_first_name, '') || ' ' || COALESCE(p.rider_last_name, '')), ''),
                                p.purchaser_name) AS RiderName,
                       p.purchaser_name AS PurchaserName, p.purchaser_email AS PurchaserEmail,
                       p.redemption_token AS RedemptionToken, p.amount_cents AS AmountCents
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                JOIN event e ON e.id = t.event_id AND e.tenant_id = p.tenant_id
                WHERE t.event_id = @eventId AND p.tenant_id = @tenantId AND p.status IN ('paid', 'redeemed')
                ORDER BY lower(t.name), lower(COALESCE(p.rider_last_name, p.purchaser_name))";
            return (await _db.Query<LessonBookingRow>(sql, new { tenantId, eventId })).ToList();
        }

        public async Task<List<LessonBikeRow>> ListBikes(Guid tenantId, Guid eventId)
        {
            const string sql = @"
                SELECT r.id AS RentalId, r.status, COALESCE(NULLIF(TRIM(r.renter_name), ''), r.renter_email, '(walk-in)') AS RenterName,
                       COALESCE((SELECT string_agg(l.quantity || ' x ' || l.name_snapshot
                                     || COALESCE(' (' || l.variant_label || ')', ''), ', ' ORDER BY l.name_snapshot)
                                 FROM shop_rental_line l WHERE l.rental_id = r.id), '') AS Items,
                       r.rider_height_in AS RiderHeightIn, r.rider_weight_lb AS RiderWeightLb
                FROM shop_rental r
                WHERE r.tenant_id = @tenantId AND r.event_id = @eventId
                  AND r.status IN ('pending', 'paid', 'out', 'returned')
                ORDER BY lower(COALESCE(r.renter_name, r.renter_email, ''))";
            return (await _db.Query<LessonBikeRow>(sql, new { tenantId, eventId })).ToList();
        }

        public async Task<bool> WeatherCancel(Guid tenantId, Guid eventId, Guid byUserId)
        {
            const string sql = @"
                UPDATE event SET status = 'cancelled', weather_cancelled_at = now(), weather_cancelled_by_user_id = @byUserId
                WHERE id = @eventId AND tenant_id = @tenantId AND weather_cancelled_at IS NULL";
            return await _db.Execute(sql, new { tenantId, eventId, byUserId }) > 0;
        }
    }
}
