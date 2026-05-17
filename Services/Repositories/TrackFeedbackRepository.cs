using Services.Helpers.Interfaces;
using Services.Repositories.Data.FeedbackData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TrackFeedbackRepository : ITrackFeedbackRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, user_id AS UserId,
            name, email, rating, body, status, admin_notes AS AdminNotes,
            actioned_by_user_id AS ActionedByUserId,
            actioned_at_utc AS ActionedAtUtc,
            ip_address AS IpAddress, user_agent AS UserAgent,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;
        public TrackFeedbackRepository(IDbHelper db) => _db = db;

        public async Task<Guid> Create(TrackFeedback feedback)
        {
            const string sql = @"
                INSERT INTO track_feedback
                    (tenant_id, user_id, name, email, rating, body, ip_address, user_agent)
                VALUES
                    (@TenantId, @UserId, @Name, @Email, @Rating, @Body, @IpAddress, @UserAgent)
                RETURNING id";
            return (await _db.Query<Guid>(sql, feedback)).First();
        }

        public async Task<TrackFeedback?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM track_feedback WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<TrackFeedback>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<List<TrackFeedback>> ListByTenant(Guid tenantId, string? statusFilter, int limit, int offset)
        {
            // statusFilter null = include every status; otherwise exact-match.
            var where = statusFilter is null ? "" : "AND status = @statusFilter";
            var sql = $@"
                SELECT {Columns}
                FROM track_feedback
                WHERE tenant_id = @tenantId {where}
                ORDER BY created_at DESC
                LIMIT @limit OFFSET @offset";
            var rows = await _db.Query<TrackFeedback>(sql, new { tenantId, statusFilter, limit, offset });
            return rows.ToList();
        }

        public async Task<int> CountByTenant(Guid tenantId, string? statusFilter)
        {
            var where = statusFilter is null ? "" : "AND status = @statusFilter";
            var sql = $"SELECT COUNT(*) FROM track_feedback WHERE tenant_id = @tenantId {where}";
            return await _db.ExecuteScalar(sql, new { tenantId, statusFilter });
        }

        public async Task UpdateStatus(Guid id, Guid tenantId, string status, string? adminNotes, Guid actionedByUserId)
        {
            const string sql = @"
                UPDATE track_feedback
                SET status = @status,
                    admin_notes = @adminNotes,
                    actioned_by_user_id = @actionedByUserId,
                    actioned_at_utc = now()
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, status, adminNotes, actionedByUserId });
        }
    }
}
