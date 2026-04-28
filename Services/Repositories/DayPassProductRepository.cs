using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class DayPassProductRepository : IDayPassProductRepository
    {
        private const string SelectColumns = @"
            id, tenant_id AS TenantId, name, description,
            price_cents AS PriceCents, is_active AS IsActive, sort_order AS SortOrder,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public DayPassProductRepository(IDbHelper db) => _db = db;

        public async Task<List<DayPassProduct>> GetAllForTenant(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? " AND is_active = true" : "";
            var sql = $@"
                SELECT {SelectColumns}
                FROM day_pass_product
                WHERE tenant_id = @tenantId {filter}
                ORDER BY sort_order, name";
            var result = await _db.Query<DayPassProduct>(sql, new { tenantId });
            return result.ToList();
        }

        public async Task<DayPassProduct?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {SelectColumns} FROM day_pass_product WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            var result = await _db.Query<DayPassProduct>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<Guid> Create(DayPassProduct p)
        {
            const string sql = @"
                INSERT INTO day_pass_product (tenant_id, name, description, price_cents, is_active, sort_order)
                VALUES (@TenantId, @Name, @Description, @PriceCents, @IsActive, @SortOrder)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, p);
            return result.First();
        }

        public async Task Update(DayPassProduct p)
        {
            const string sql = @"
                UPDATE day_pass_product
                SET name = @Name, description = @Description, price_cents = @PriceCents,
                    is_active = @IsActive, sort_order = @SortOrder
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, p);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM day_pass_product WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }
    }
}
