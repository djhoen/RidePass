using Services.Helpers.Interfaces;
using Services.Repositories.Data.PricingData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class DayPriceRuleRepository : IDayPriceRuleRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, label, weekday, on_date AS OnDate, event_type_id AS EventTypeId,
            adjust_kind AS AdjustKind, adjust_value AS AdjustValue, is_active AS IsActive,
            created_by_user_id AS CreatedByUserId, created_at AS CreatedAt";

        private readonly IDbHelper _db;

        public DayPriceRuleRepository(IDbHelper db) => _db = db;

        public async Task<List<DayPriceRule>> List(Guid tenantId, bool activeOnly)
        {
            var sql = $@"
                SELECT {Columns} FROM day_price_rule
                WHERE tenant_id = @tenantId {(activeOnly ? "AND is_active" : "")}
                ORDER BY on_date NULLS FIRST, weekday, lower(label)";
            return (await _db.Query<DayPriceRule>(sql, new { tenantId })).ToList();
        }

        public async Task<Guid> Create(DayPriceRule r)
        {
            // An event type, when named, must be this venue's; otherwise nothing is inserted.
            const string sql = @"
                INSERT INTO day_price_rule (tenant_id, label, weekday, on_date, event_type_id, adjust_kind, adjust_value,
                                            is_active, created_by_user_id)
                SELECT @TenantId, @Label, @Weekday, @OnDate, @EventTypeId, @AdjustKind, @AdjustValue, true, @CreatedByUserId
                WHERE @EventTypeId IS NULL
                   OR EXISTS (SELECT 1 FROM tenant_event_type et WHERE et.id = @EventTypeId AND et.tenant_id = @TenantId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, r)).FirstOrDefault();
        }

        public async Task<int> SetActive(Guid tenantId, Guid id, bool isActive)
        {
            const string sql = "UPDATE day_price_rule SET is_active = @isActive WHERE id = @id AND tenant_id = @tenantId";
            return await _db.Execute(sql, new { tenantId, id, isActive });
        }

        public async Task<int> Delete(Guid tenantId, Guid id)
        {
            const string sql = "DELETE FROM day_price_rule WHERE id = @id AND tenant_id = @tenantId";
            return await _db.Execute(sql, new { tenantId, id });
        }
    }
}
