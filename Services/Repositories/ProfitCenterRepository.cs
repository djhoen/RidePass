using Services.Helpers.Interfaces;
using Services.Repositories.Data.AccountingData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class ProfitCenterRepository : IProfitCenterRepository
    {
        private readonly IDbHelper _db;

        public ProfitCenterRepository(IDbHelper db) => _db = db;

        public async Task<List<ProfitCenter>> ListForTenant(Guid tenantId)
        {
            const string sql = @"
                SELECT id, tenant_id AS TenantId, name, sort_order AS SortOrder, color
                FROM profit_center
                WHERE tenant_id = @tenantId
                ORDER BY sort_order, lower(name)";
            return (await _db.Query<ProfitCenter>(sql, new { tenantId })).ToList();
        }

        public async Task<List<ProfitCenterAssignment>> ListAssignments(Guid tenantId)
        {
            const string sql = @"
                SELECT revenue_key AS RevenueKey, profit_center_id AS ProfitCenterId
                FROM profit_center_revenue_key
                WHERE tenant_id = @tenantId";
            return (await _db.Query<ProfitCenterAssignment>(sql, new { tenantId })).ToList();
        }

        public async Task<ProfitCenter?> GetById(Guid id, Guid tenantId)
        {
            const string sql = @"
                SELECT id, tenant_id AS TenantId, name, sort_order AS SortOrder, color
                FROM profit_center
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<ProfitCenter>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> Create(Guid tenantId, string name, int sortOrder, string color)
        {
            const string sql = @"
                INSERT INTO profit_center (tenant_id, name, sort_order, color)
                VALUES (@tenantId, @name, @sortOrder, @color)
                RETURNING id";
            return (await _db.Query<Guid>(sql, new { tenantId, name, sortOrder, color })).First();
        }

        public async Task Update(Guid id, Guid tenantId, string name, string color)
        {
            const string sql = @"
                UPDATE profit_center
                SET name = @name, color = @color, updated_at = now()
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, name, color });
        }

        // Assignments cascade away with the row, so the deleted center's slots fall back to their
        // built-in departments rather than dangling.
        public async Task Delete(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM profit_center WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task UpdateSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            const string sql = @"
                UPDATE profit_center AS p
                SET sort_order = data.sort_order, updated_at = now()
                FROM (SELECT unnest(@ids::uuid[]) AS id,
                             unnest(@orders::int[]) AS sort_order) AS data
                WHERE p.id = data.id AND p.tenant_id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId,
                ids = ids.ToArray(),
                orders = sortOrders.ToArray(),
            });
        }

        // The WHERE EXISTS makes cross-tenant assignment impossible at the SQL layer even if a
        // caller forgets to verify ownership: pointing at another tenant's center inserts nothing.
        public async Task UpsertAssignment(Guid tenantId, string revenueKey, Guid profitCenterId)
        {
            const string sql = @"
                INSERT INTO profit_center_revenue_key (tenant_id, revenue_key, profit_center_id)
                SELECT @tenantId, @revenueKey, @profitCenterId
                WHERE EXISTS (SELECT 1 FROM profit_center
                              WHERE id = @profitCenterId AND tenant_id = @tenantId)
                ON CONFLICT (tenant_id, revenue_key)
                DO UPDATE SET profit_center_id = EXCLUDED.profit_center_id";
            await _db.Execute(sql, new { tenantId, revenueKey, profitCenterId });
        }

        public async Task ClearAssignment(Guid tenantId, string revenueKey)
        {
            const string sql = @"
                DELETE FROM profit_center_revenue_key
                WHERE tenant_id = @tenantId AND revenue_key = @revenueKey";
            await _db.Execute(sql, new { tenantId, revenueKey });
        }
    }
}
