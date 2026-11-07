using Dapper;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.WristbandData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RiderDayBandRepository : IRiderDayBandRepository
    {
        private readonly IDbHelper _db;
        public RiderDayBandRepository(IDbHelper db) => _db = db;

        private const string Columns = @"
            b.id, b.tenant_id AS TenantId, b.business_date AS BusinessDate, b.code, b.owner_key AS OwnerKey,
            b.rider_key AS RiderKey, b.rider_user_id AS RiderUserId, b.rider_name AS RiderName,
            b.season_pass_purchase_id AS SeasonPassPurchaseId, b.issued_by_user_id AS IssuedByUserId,
            NULLIF(TRIM(CONCAT_WS(' ', iu.first_name, iu.last_name)), '') AS IssuedByName,
            b.issued_at AS IssuedAt, b.voided_at AS VoidedAt, b.voided_by_user_id AS VoidedByUserId,
            b.void_reason AS VoidReason, b.replaced_by_band_id AS ReplacedByBandId";

        private const string From = @"
            FROM rider_day_band b
            LEFT JOIN users iu ON iu.id = b.issued_by_user_id";

        public async Task<RiderDayBand?> GetActiveForRider(Guid tenantId, DateOnly date, string ownerKey, string riderKey) =>
            (await _db.Query<RiderDayBand>($@"SELECT {Columns} {From}
                WHERE b.tenant_id = @tenantId AND b.business_date = @date AND b.owner_key = @ownerKey
                  AND b.rider_key = @riderKey AND b.voided_at IS NULL",
                new { tenantId, date, ownerKey, riderKey })).FirstOrDefault();

        public async Task<RiderDayBand?> GetActiveByCode(Guid tenantId, DateOnly date, string code) =>
            (await _db.Query<RiderDayBand>($@"SELECT {Columns} {From}
                WHERE b.tenant_id = @tenantId AND b.business_date = @date AND lower(b.code) = lower(@code)
                  AND b.voided_at IS NULL",
                new { tenantId, date, code })).FirstOrDefault();

        private const string InsertBand = @"
            INSERT INTO rider_day_band (id, tenant_id, business_date, code, owner_key, rider_key, rider_user_id,
                                        rider_name, season_pass_purchase_id, issued_by_user_id)
            VALUES (@Id, @TenantId, @BusinessDate, @Code, @OwnerKey, @RiderKey, @RiderUserId,
                    @RiderName, @SeasonPassPurchaseId, @IssuedByUserId)";

        private const string InsertLink = @"
            INSERT INTO rider_day_band_admission (tenant_id, band_id, season_pass_reservation_id, event_ticket_purchase_id,
                                                  linked_by_user_id)
            SELECT @tenantId, @bandId, @reservationId, @ticketId, @staffUserId
             WHERE NOT EXISTS (SELECT 1 FROM rider_day_band_admission
                                WHERE band_id = @bandId AND tenant_id = @tenantId
                                  AND (season_pass_reservation_id = @reservationId OR event_ticket_purchase_id = @ticketId))";

        private async Task<RiderDayBand?> Reload(Guid id, Guid tenantId) =>
            (await _db.Query<RiderDayBand>($"SELECT {Columns} {From} WHERE b.id = @id AND b.tenant_id = @tenantId",
                new { id, tenantId })).FirstOrDefault();

        public async Task<RiderDayBand?> Issue(RiderDayBand band, Guid? reservationId, Guid? ticketId)
        {
            if (band.Id == Guid.Empty) band.Id = Guid.NewGuid();
            try
            {
                await _db.WithTransaction(async (conn, tx) =>
                {
                    await conn.ExecuteAsync(InsertBand, band, tx);
                    if (reservationId is not null || ticketId is not null)
                        await conn.ExecuteAsync(InsertLink, new
                        {
                            tenantId = band.TenantId, bandId = band.Id, reservationId, ticketId, staffUserId = band.IssuedByUserId,
                        }, tx);
                    return true;
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return null;
            }
            return await Reload(band.Id, band.TenantId);
        }

        public async Task<RiderDayBand?> Replace(Guid oldBandId, RiderDayBand band, string reason, Guid? staffUserId)
        {
            if (band.Id == Guid.Empty) band.Id = Guid.NewGuid();
            try
            {
                var ok = await _db.WithTransaction(async (conn, tx) =>
                {
                    // Void first, so the rider's one-active-band slot is free for the replacement.
                    var voided = await conn.ExecuteAsync(@"
                        UPDATE rider_day_band SET voided_at = now(), voided_by_user_id = @staffUserId, void_reason = @reason
                         WHERE id = @oldBandId AND tenant_id = @tenantId AND voided_at IS NULL",
                        new { oldBandId, tenantId = band.TenantId, staffUserId, reason }, tx);
                    if (voided != 1) return false;
                    await conn.ExecuteAsync(InsertBand, band, tx);
                    await conn.ExecuteAsync(@"
                        UPDATE rider_day_band SET replaced_by_band_id = @newId WHERE id = @oldBandId AND tenant_id = @tenantId;
                        UPDATE rider_day_band_admission SET band_id = @newId WHERE band_id = @oldBandId AND tenant_id = @tenantId;",
                        new { oldBandId, newId = band.Id, tenantId = band.TenantId }, tx);
                    return true;
                });
                if (!ok) return null;
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return null;
            }
            return await Reload(band.Id, band.TenantId);
        }

        public Task LinkAdmission(Guid tenantId, Guid bandId, Guid? reservationId, Guid? ticketId, Guid? staffUserId) =>
            _db.Execute(InsertLink + @"
               AND EXISTS (SELECT 1 FROM rider_day_band WHERE id = @bandId AND tenant_id = @tenantId AND voided_at IS NULL)",
                new { tenantId, bandId, reservationId, ticketId, staffUserId });

        public Task<int> UnlinkReservation(Guid tenantId, Guid reservationId) => _db.Execute(@"
            DELETE FROM rider_day_band_admission WHERE tenant_id = @tenantId AND season_pass_reservation_id = @reservationId",
            new { tenantId, reservationId });

        public async Task<bool> Covers(Guid tenantId, Guid bandId, Guid reservationId) =>
            await _db.ExecuteScalar(@"
                SELECT count(*)::int FROM rider_day_band_admission
                 WHERE tenant_id = @tenantId AND band_id = @bandId AND season_pass_reservation_id = @reservationId",
                new { tenantId, bandId, reservationId }) > 0;

        public async Task<List<RiderDayBandAdmissionRow>> ListAdmissions(Guid tenantId, Guid bandId) =>
            (await _db.Query<RiderDayBandAdmissionRow>(@"
                SELECT a.band_id AS BandId, a.season_pass_reservation_id AS SeasonPassReservationId,
                       a.event_ticket_purchase_id AS EventTicketPurchaseId,
                       COALESCE(r.event_id, tier.event_id) AS EventId,
                       COALESCE(e.title, CASE WHEN r.id IS NOT NULL THEN 'Walk-up admission' END) AS EventTitle,
                       COALESCE(r.status, t.status) AS Status,
                       COALESCE(r.checked_in_at, t.redeemed_at_utc) AS CheckedInAt,
                       a.linked_at AS LinkedAt
                FROM rider_day_band_admission a
                LEFT JOIN season_pass_reservation r ON r.id = a.season_pass_reservation_id
                LEFT JOIN event_ticket_purchase t ON t.id = a.event_ticket_purchase_id AND t.tenant_id = @tenantId
                LEFT JOIN event_ticket_tier tier ON tier.id = t.tier_id
                LEFT JOIN event e ON e.id = COALESCE(r.event_id, tier.event_id) AND e.tenant_id = @tenantId
                WHERE a.tenant_id = @tenantId AND a.band_id = @bandId
                ORDER BY a.linked_at",
                new { tenantId, bandId })).ToList();

        public async Task<Dictionary<Guid, string>> GetActiveCodesForReservations(IEnumerable<Guid> reservationIds, Guid tenantId)
        {
            var ids = reservationIds.Distinct().ToArray();
            if (ids.Length == 0) return new Dictionary<Guid, string>();
            var rows = await _db.Query<(Guid ReservationId, string Code)>(@"
                SELECT a.season_pass_reservation_id AS ReservationId, b.code AS Code
                FROM rider_day_band_admission a
                JOIN rider_day_band b ON b.id = a.band_id AND b.tenant_id = @tenantId AND b.voided_at IS NULL
                WHERE a.tenant_id = @tenantId AND a.season_pass_reservation_id = ANY(@ids)",
                new { ids, tenantId });
            return rows.GroupBy(r => r.ReservationId).ToDictionary(g => g.Key, g => g.First().Code);
        }
    }
}
