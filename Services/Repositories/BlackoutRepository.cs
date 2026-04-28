using Services.Helpers.Interfaces;
using Services.Repositories.Data.EventData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class BlackoutRepository : IBlackoutRepository
    {
        private const string SelectColumns = @"
            id, tenant_id AS TenantId,
            starts_at AS StartsAt, ends_at AS EndsAt, all_day AS AllDay, reason,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public BlackoutRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<List<Blackout>> GetInRange(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            var sql = $@"
                SELECT {SelectColumns}
                FROM blackout
                WHERE tenant_id = @tenantId
                  AND starts_at < @toUtc
                  AND ends_at >= @fromUtc
                ORDER BY starts_at, id";
            var result = await _db.Query<Blackout>(sql, new { tenantId, fromUtc, toUtc });
            return result.ToList();
        }

        public async Task<Blackout?> GetById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectColumns}
                FROM blackout
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<Blackout>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<Guid> Create(Blackout blackout)
        {
            const string sql = @"
                INSERT INTO blackout (tenant_id, starts_at, ends_at, all_day, reason)
                VALUES (@TenantId, @StartsAt, @EndsAt, @AllDay, @Reason)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, blackout);
            return result.First();
        }

        public async Task Update(Blackout blackout)
        {
            const string sql = @"
                UPDATE blackout
                SET starts_at = @StartsAt,
                    ends_at   = @EndsAt,
                    all_day   = @AllDay,
                    reason    = @Reason
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, blackout);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM blackout WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }
    }
}
