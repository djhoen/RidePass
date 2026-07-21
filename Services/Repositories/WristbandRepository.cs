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
                       tt.name AS TierName, '' AS EventTitle, '' AS Code, 'x' AS TicketStatus
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

        public async Task<WristbandResolution?> ResolveCode(Guid tenantId, string code)
        {
            // Codes repeat across events (cheap number packs restart ranges), so prefer the event
            // that's actually happening: not over for more than a day, newest start first.
            const string sql = @"
                SELECT w.ticket_id AS TicketId, w.event_id AS EventId, w.code, w.linked_at AS LinkedAt,
                       t.redemption_token AS RedemptionToken, t.status AS TicketStatus,
                       t.rider_first_name AS RiderFirstName, t.rider_last_name AS RiderLastName,
                       t.purchaser_name AS PurchaserName, t.race_number AS RaceNumber,
                       tt.name AS TierName, e.title AS EventTitle
                FROM event_wristband w
                JOIN event_ticket_purchase t ON t.id = w.ticket_id
                JOIN event_ticket_tier tt ON tt.id = t.tier_id
                JOIN event e ON e.id = w.event_id
                WHERE w.tenant_id = @tenantId AND lower(w.code) = lower(@code)
                  AND e.ends_at > now() - interval '1 day'
                ORDER BY e.starts_at DESC
                LIMIT 1";
            return (await _db.Query<WristbandResolution>(sql, new { tenantId, code })).FirstOrDefault();
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
            return rows.ToDictionary(r => r.TicketId, r => r.Code);
        }
    }
}
