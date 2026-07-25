using Services.Helpers.Interfaces;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class StaffAlertScanRepository : IStaffAlertScanRepository
    {
        private readonly IDbHelper _db;

        public StaffAlertScanRepository(IDbHelper db) => _db = db;

        public async Task<Guid?> TryClaimScan(Guid tenantId, DateOnly scanDate)
        {
            // ON CONFLICT DO NOTHING against uk_staff_alert_scan_tenant_day is the whole
            // concurrency story: the winner gets an id back, the loser gets no rows and stops.
            const string sql = @"
                INSERT INTO staff_alert_scan (tenant_id, scan_date)
                VALUES (@tenantId, @scanDate)
                ON CONFLICT (tenant_id, scan_date) DO NOTHING
                RETURNING id";
            var rows = await _db.Query<Guid>(sql, new { tenantId, scanDate });
            return rows.Count() == 0 ? null : rows.First();
        }

        /// <summary>
        /// Scoped by id alone, which is safe here in a way it would not be on a request-driven
        /// path: scanId is never supplied by a caller, it is the value TryClaimScan just returned
        /// from its own INSERT for this tenant, so the row is known to belong to them. The columns
        /// written are bookkeeping about that scan and carry no tenant data.
        /// </summary>
        public async Task CompleteScan(Guid scanId, int flaggedCount, DateTime? sentAtUtc)
        {
            const string sql = @"
                UPDATE staff_alert_scan
                SET flagged_count = @flaggedCount, sent_at = @sentAtUtc
                WHERE id = @scanId";
            await _db.Execute(sql, new { scanId, flaggedCount, sentAtUtc });
        }

        public async Task<DateOnly?> GetLastScanDate(Guid tenantId)
        {
            const string sql = @"
                SELECT scan_date FROM staff_alert_scan
                WHERE tenant_id = @tenantId
                ORDER BY scan_date DESC
                LIMIT 1";
            var rows = await _db.Query<DateOnly>(sql, new { tenantId });
            return rows.Count() == 0 ? null : rows.First();
        }
    }
}
