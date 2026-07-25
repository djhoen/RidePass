using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class WristbandRepository : IWristbandRepository
    {
        private readonly IDbHelper _db;
        public WristbandRepository(IDbHelper db) => _db = db;

        public async Task<string?> Link(Guid tenantId, Guid eventId, Guid ticketId, string code, Guid? byUserId)
        {
            // Whose wrist is this code already on? Same ticket = idempotent re-scan; someone else =
            // refuse with a name so staff can sort it out at the gate.
            var existing = (await _db.Query<WristbandResolution>(@"
                SELECT w.ticket_id AS TicketId,
                       COALESCE(NULLIF(TRIM(COALESCE(t.rider_first_name,'') || ' ' || COALESCE(t.rider_last_name,'')), ''),
                                t.purchaser_name) AS PurchaserName,
                       tt.name AS TierName, '' AS EventTitle, '' AS Code, 'x' AS Status, 'ticket' AS Source
                FROM event_wristband w
                JOIN event_ticket_purchase t ON t.id = w.ticket_id
                JOIN event_ticket_tier tt ON tt.id = t.tier_id
                WHERE w.tenant_id = @tenantId AND w.event_id = @eventId AND lower(w.code) = lower(@code)",
                new { tenantId, eventId, code })).FirstOrDefault();
            if (existing is not null)
            {
                if (existing.TicketId == ticketId) return null;   // already linked to this entrant
                return $"{existing.PurchaserName} ({existing.TierName})";
            }

            // Replace-then-insert in one transaction: the entrant wears exactly one band, and the
            // old (lost) band must stop resolving the moment the new one is linked. A concurrent
            // link of the same code races the unique index; the loser surfaces as a conflict.
            try
            {
                await _db.ExecuteBatch(new List<(string Sql, object? Param)>
                {
                    ("DELETE FROM event_wristband WHERE ticket_id = @ticketId AND tenant_id = @tenantId",
                        new { ticketId, tenantId }),
                    (@"INSERT INTO event_wristband (tenant_id, event_id, ticket_id, code, linked_by_user_id)
                       VALUES (@tenantId, @eventId, @ticketId, @code, @byUserId)",
                        new { tenantId, eventId, ticketId, code, byUserId }),
                });
                return null;
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return "another entrant (it was just linked)";
            }
        }

        public Task<int> UnlinkTicket(Guid ticketId, Guid tenantId) => _db.Execute(
            "DELETE FROM event_wristband WHERE ticket_id = @ticketId AND tenant_id = @tenantId",
            new { ticketId, tenantId });

        public async Task<WristbandResolution?> ResolveCode(Guid tenantId, string code, DateOnly todayLocal)
        {
            // Codes repeat across events and days (cheap number packs restart ranges), so prefer the
            // scope that's actually live: an event not over for more than a day, or a walk-up band
            // issued for today. Newest first when several match.
            const string sql = @"
                SELECT 'ticket' AS Source, w.ticket_id AS TicketId, w.event_id AS EventId,
                       NULL::uuid AS ReservationId, NULL::uuid AS PassPurchaseId,
                       w.code, w.linked_at AS LinkedAt,
                       t.redemption_token AS RedemptionToken, t.status AS Status,
                       t.rider_first_name AS RiderFirstName, t.rider_last_name AS RiderLastName,
                       t.purchaser_name AS PurchaserName, t.race_number AS RaceNumber,
                       tt.name AS TierName, e.title AS EventTitle, e.starts_at AS SortKey
                FROM event_wristband w
                JOIN event_ticket_purchase t ON t.id = w.ticket_id
                JOIN event_ticket_tier tt ON tt.id = t.tier_id
                JOIN event e ON e.id = w.event_id
                WHERE w.tenant_id = @tenantId AND lower(w.code) = lower(@code)
                  AND e.ends_at > now() - interval '1 day'

                UNION ALL

                SELECT 'season_pass' AS Source, NULL::uuid AS TicketId, w.event_id AS EventId,
                       w.season_pass_reservation_id AS ReservationId, r.season_pass_purchase_id AS PassPurchaseId,
                       w.code, w.linked_at AS LinkedAt,
                       p.redemption_token AS RedemptionToken, p.status AS Status,
                       NULL AS RiderFirstName, NULL AS RiderLastName,
                       p.purchaser_name AS PurchaserName, NULL AS RaceNumber,
                       NULL AS TierName, COALESCE(e2.title, 'Walk-up admission') AS EventTitle,
                       COALESCE(e2.starts_at, w.linked_at) AS SortKey
                FROM event_wristband w
                JOIN season_pass_reservation r ON r.id = w.season_pass_reservation_id
                JOIN season_pass_purchase p ON p.id = r.season_pass_purchase_id
                LEFT JOIN event e2 ON e2.id = w.event_id
                WHERE w.tenant_id = @tenantId AND p.tenant_id = @tenantId AND lower(w.code) = lower(@code)
                  AND (
                        (w.event_id IS NOT NULL AND e2.ends_at > now() - interval '1 day')
                     OR (w.event_id IS NULL AND w.valid_on_date = @todayLocal)
                      )

                ORDER BY SortKey DESC
                LIMIT 1";
            return (await _db.Query<WristbandResolution>(sql, new { tenantId, code, todayLocal })).FirstOrDefault();
        }

        /// <summary>Links a band code to a season pass admission, replacing any band that admission
        /// already wears. Returns null on success, or the other holder's name when the code is
        /// already on someone else within the same scope (the event, or the tenant-local date).</summary>
        public async Task<string?> LinkToReservation(Guid tenantId, Guid reservationId, Guid? eventId,
            DateOnly? validOnDate, string code, Guid? byUserId)
        {
            // Same conflict question as the ticket branch, asked within whichever scope this row
            // will occupy. Same reservation = idempotent re-scan; someone else = refuse with a name.
            var existing = eventId is not null
                ? (await _db.Query<WristbandResolution>(@"
                    SELECT 'season_pass' AS Source, w.season_pass_reservation_id AS ReservationId,
                           p.purchaser_name AS PurchaserName, '' AS EventTitle, '' AS Code, 'x' AS Status
                    FROM event_wristband w
                    JOIN season_pass_reservation r ON r.id = w.season_pass_reservation_id
                    JOIN season_pass_purchase p ON p.id = r.season_pass_purchase_id
                    WHERE w.tenant_id = @tenantId AND p.tenant_id = @tenantId
                      AND w.event_id = @eventId AND lower(w.code) = lower(@code)",
                    new { tenantId, eventId, code })).FirstOrDefault()
                : (await _db.Query<WristbandResolution>(@"
                    SELECT 'season_pass' AS Source, w.season_pass_reservation_id AS ReservationId,
                           p.purchaser_name AS PurchaserName, '' AS EventTitle, '' AS Code, 'x' AS Status
                    FROM event_wristband w
                    JOIN season_pass_reservation r ON r.id = w.season_pass_reservation_id
                    JOIN season_pass_purchase p ON p.id = r.season_pass_purchase_id
                    WHERE w.tenant_id = @tenantId AND p.tenant_id = @tenantId
                      AND w.event_id IS NULL AND w.valid_on_date = @validOnDate
                      AND lower(w.code) = lower(@code)",
                    new { tenantId, validOnDate, code })).FirstOrDefault();

            if (existing is not null)
            {
                if (existing.ReservationId == reservationId) return null;   // already on this admission
                return existing.PurchaserName;
            }

            try
            {
                await _db.ExecuteBatch(new List<(string Sql, object? Param)>
                {
                    ("DELETE FROM event_wristband WHERE season_pass_reservation_id = @reservationId AND tenant_id = @tenantId",
                        new { reservationId, tenantId }),
                    (@"INSERT INTO event_wristband
                           (tenant_id, event_id, season_pass_reservation_id, valid_on_date, code, linked_by_user_id)
                       VALUES (@tenantId, @eventId, @reservationId, @validOnDate, @code, @byUserId)",
                        new { tenantId, eventId, reservationId, validOnDate, code, byUserId }),
                });
                return null;
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return "another entrant (it was just linked)";
            }
        }

        public Task<int> UnlinkReservation(Guid reservationId, Guid tenantId) => _db.Execute(
            "DELETE FROM event_wristband WHERE season_pass_reservation_id = @reservationId AND tenant_id = @tenantId",
            new { reservationId, tenantId });

        public async Task<Dictionary<Guid, string>> GetCodesForReservations(IEnumerable<Guid> reservationIds, Guid tenantId)
        {
            var ids = reservationIds.Distinct().ToArray();
            if (ids.Length == 0) return new Dictionary<Guid, string>();
            var rows = await _db.Query<EventWristband>(@"
                SELECT season_pass_reservation_id AS SeasonPassReservationId, code
                FROM event_wristband
                WHERE season_pass_reservation_id = ANY(@ids) AND tenant_id = @tenantId",
                new { ids, tenantId });
            // Non-null on every row: the query filters on season_pass_reservation_id = ANY(@ids).
            return rows.ToDictionary(r => r.SeasonPassReservationId!.Value, r => r.Code);
        }

        public async Task<Dictionary<Guid, string>> GetCodesForTickets(IEnumerable<Guid> ticketIds, Guid tenantId)
        {
            var ids = ticketIds.Distinct().ToArray();
            if (ids.Length == 0) return new Dictionary<Guid, string>();
            var rows = await _db.Query<EventWristband>(@"
                SELECT ticket_id AS TicketId, code
                FROM event_wristband
                WHERE ticket_id = ANY(@ids) AND tenant_id = @tenantId",
                new { ids, tenantId });
            // TicketId is non-null on every row here: the query filters on ticket_id = ANY(@ids).
            return rows.ToDictionary(r => r.TicketId!.Value, r => r.Code);
        }
    }
}
