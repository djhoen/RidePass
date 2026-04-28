using Services.Helpers.Interfaces;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantEventTypeRepository : ITenantEventTypeRepository
    {
        private const string SelectColumns = @"
            id, tenant_id AS TenantId, code, name, color,
            sort_order AS SortOrder, is_system AS IsSystem,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public TenantEventTypeRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<List<TenantEventType>> GetAllForTenant(Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectColumns}
                FROM tenant_event_type
                WHERE tenant_id = @tenantId
                ORDER BY sort_order, name";
            var result = await _db.Query<TenantEventType>(sql, new { tenantId });
            return result.ToList();
        }

        public async Task<TenantEventType?> GetById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectColumns}
                FROM tenant_event_type
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<TenantEventType>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<Guid> Create(TenantEventType type)
        {
            const string sql = @"
                INSERT INTO tenant_event_type (tenant_id, code, name, color, sort_order, is_system)
                VALUES (@TenantId, @Code, @Name, @Color, @SortOrder, @IsSystem)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, type);
            return result.First();
        }

        public async Task Update(Guid id, Guid tenantId, string name, string color, int sortOrder)
        {
            const string sql = @"
                UPDATE tenant_event_type
                SET name = @name, color = @color, sort_order = @sortOrder
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, name, color, sortOrder });
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM tenant_event_type WHERE id = @id AND tenant_id = @tenantId AND is_system = false";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task<bool> IsInUseByEvents(Guid id, Guid tenantId)
        {
            const string sql = @"
                SELECT COUNT(*)
                FROM event
                WHERE event_type_id = @id AND tenant_id = @tenantId";
            var count = await _db.ExecuteScalar(sql, new { id, tenantId });
            return count > 0;
        }
    }
}
