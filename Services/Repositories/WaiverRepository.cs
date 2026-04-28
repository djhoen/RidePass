using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class WaiverRepository : IWaiverRepository
    {
        private const string WaiverColumns = @"
            id, tenant_id AS TenantId, version, title, body,
            is_active AS IsActive, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public WaiverRepository(IDbHelper db) => _db = db;

        public async Task<TenantWaiver?> GetActive(Guid tenantId)
        {
            var sql = $@"
                SELECT {WaiverColumns}
                FROM tenant_waiver
                WHERE tenant_id = @tenantId AND is_active = true
                LIMIT 1";
            var result = await _db.Query<TenantWaiver>(sql, new { tenantId });
            return result.FirstOrDefault();
        }

        public async Task<TenantWaiver?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {WaiverColumns} FROM tenant_waiver WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            var result = await _db.Query<TenantWaiver>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<TenantWaiver> PublishNewVersion(Guid tenantId, string title, string body)
        {
            // Two steps to avoid a CTE snapshot-visibility race with the partial unique index
            // `uk_tenant_waiver_active`: if the CTE's INSERT doesn't see the CTE's UPDATE yet,
            // the new active row collides with the still-active old row.
            // If the INSERT fails, admin can retry — UPDATE is idempotent.

            await _db.Execute(
                "UPDATE tenant_waiver SET is_active = false WHERE tenant_id = @tenantId AND is_active = true",
                new { tenantId });

            const string insertSql = @"
                INSERT INTO tenant_waiver (tenant_id, version, title, body, is_active)
                SELECT @tenantId, COALESCE(MAX(version), 0) + 1, @title, @body, true
                FROM tenant_waiver
                WHERE tenant_id = @tenantId
                RETURNING id, tenant_id AS TenantId, version, title, body,
                          is_active AS IsActive, created_at AS CreatedAt, updated_at AS UpdatedAt";

            var result = await _db.Query<TenantWaiver>(insertSql, new { tenantId, title, body });
            return result.First();
        }

        public async Task<RiderWaiverSignature?> GetSignature(Guid userId, Guid waiverId)
        {
            const string sql = @"
                SELECT id, tenant_id AS TenantId, user_id AS UserId, waiver_id AS WaiverId,
                       signed_at AS SignedAt, ip_address AS IpAddress
                FROM rider_waiver_signature
                WHERE user_id = @userId AND waiver_id = @waiverId
                LIMIT 1";
            var result = await _db.Query<RiderWaiverSignature>(sql, new { userId, waiverId });
            return result.FirstOrDefault();
        }

        public async Task<Guid> Sign(Guid tenantId, Guid userId, Guid waiverId, string? ipAddress)
        {
            const string sql = @"
                INSERT INTO rider_waiver_signature (tenant_id, user_id, waiver_id, ip_address)
                VALUES (@tenantId, @userId, @waiverId, @ipAddress)
                ON CONFLICT (user_id, waiver_id) DO UPDATE SET signed_at = EXCLUDED.signed_at
                RETURNING id";
            var result = await _db.Query<Guid>(sql, new { tenantId, userId, waiverId, ipAddress });
            return result.First();
        }
    }
}
