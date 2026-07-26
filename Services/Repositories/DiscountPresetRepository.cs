using Services.Helpers.Interfaces;
using Services.Repositories.Data.DiscountData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class DiscountPresetRepository : IDiscountPresetRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, name, kind, value, surfaces,
            requires_manager AS RequiresManager, is_active AS IsActive, sort_order AS SortOrder,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public DiscountPresetRepository(IDbHelper db) => _db = db;

        public async Task<List<DiscountPreset>> ListForTenant(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? " AND is_active = true" : "";
            var sql = $@"
                SELECT {Columns} FROM discount_preset
                WHERE tenant_id = @tenantId {filter}
                ORDER BY sort_order, name";
            return (await _db.Query<DiscountPreset>(sql, new { tenantId })).ToList();
        }

        public async Task<List<DiscountPreset>> ListForSurface(Guid tenantId, string surface)
        {
            // @> is the array-contains operator: the row's surfaces must include the one asked for.
            var sql = $@"
                SELECT {Columns} FROM discount_preset
                WHERE tenant_id = @tenantId
                  AND is_active = true
                  AND surfaces @> ARRAY[@surface]::text[]
                ORDER BY sort_order, name";
            return (await _db.Query<DiscountPreset>(sql, new { tenantId, surface })).ToList();
        }

        public async Task<DiscountPreset?> Get(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM discount_preset WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<DiscountPreset>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> Create(DiscountPreset p)
        {
            const string sql = @"
                INSERT INTO discount_preset
                    (tenant_id, name, kind, value, surfaces, requires_manager, is_active, sort_order)
                VALUES
                    (@TenantId, @Name, @Kind, @Value, @Surfaces, @RequiresManager, @IsActive, @SortOrder)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        /// <summary>Returns rows affected: 0 means the row isn't this tenant's.</summary>
        public async Task<int> Update(DiscountPreset p)
        {
            const string sql = @"
                UPDATE discount_preset
                SET name = @Name, kind = @Kind, value = @Value, surfaces = @Surfaces,
                    requires_manager = @RequiresManager, is_active = @IsActive,
                    sort_order = @SortOrder, updated_at = now()
                WHERE id = @Id AND tenant_id = @TenantId";
            return await _db.Execute(sql, p);
        }

        public async Task<int> Delete(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM discount_preset WHERE id = @id AND tenant_id = @tenantId";
            return await _db.Execute(sql, new { id, tenantId });
        }
    }
}
