using Services.Helpers.Interfaces;
using Services.Repositories.Data.AuditData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class AuditLogRepository : IAuditLogRepository
    {
        private const string Columns = @"
            id, actor_user_id AS ActorUserId, actor_email AS ActorEmail, actor_role AS ActorRole,
            action, target_kind AS TargetKind, target_id AS TargetId,
            summary, metadata::text AS Metadata, ip_address AS IpAddress,
            tenant_id AS TenantId, created_at AS CreatedAt";

        private readonly IDbHelper _db;

        public AuditLogRepository(IDbHelper db) => _db = db;

        public async Task<Guid> Insert(AuditLogEntry entry)
        {
            const string sql = @"
                INSERT INTO audit_log
                    (actor_user_id, actor_email, actor_role, action, target_kind, target_id,
                     summary, metadata, ip_address, tenant_id)
                VALUES
                    (@ActorUserId, @ActorEmail, @ActorRole, @Action, @TargetKind, @TargetId,
                     @Summary, @Metadata::jsonb, @IpAddress, @TenantId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, entry)).First();
        }

        public async Task<List<AuditLogEntry>> List(
            string? action = null,
            Guid? actorUserId = null,
            string? targetKind = null,
            Guid? targetId = null,
            Guid? tenantId = null,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            int take = 200)
        {
            var sql = $@"
                SELECT {Columns}
                FROM audit_log
                WHERE (@action::text IS NULL OR action = @action)
                  AND (@actorUserId::uuid IS NULL OR actor_user_id = @actorUserId)
                  AND (@targetKind::text IS NULL OR target_kind = @targetKind)
                  AND (@targetId::uuid IS NULL OR target_id = @targetId)
                  AND (@tenantId::uuid IS NULL OR tenant_id = @tenantId)
                  AND (@fromUtc::timestamptz IS NULL OR created_at >= @fromUtc)
                  AND (@toUtc::timestamptz IS NULL OR created_at < @toUtc)
                ORDER BY created_at DESC
                LIMIT @take";
            return (await _db.Query<AuditLogEntry>(sql, new { action, actorUserId, targetKind, targetId, tenantId, fromUtc, toUtc, take })).ToList();
        }
    }
}
