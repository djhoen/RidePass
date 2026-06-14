using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class PassProductRepository : IPassProductRepository
    {
        private const string SelectColumns = @"
            id, tenant_id AS TenantId, name, description,
            price_cents AS PriceCents, is_active AS IsActive, sort_order AS SortOrder,
            requires_waiver AS RequiresWaiver,
            rider_paid_service_charge_bps AS RiderPaidServiceChargeBps,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public PassProductRepository(IDbHelper db) => _db = db;

        public async Task<List<PassProduct>> GetAllForTenant(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? " AND is_active = true" : "";
            var sql = $@"
                SELECT {SelectColumns}
                FROM pass_product
                WHERE tenant_id = @tenantId {filter}
                ORDER BY sort_order, name";
            var result = await _db.Query<PassProduct>(sql, new { tenantId });
            return result.ToList();
        }

        public async Task<bool> ExistsActiveByName(Guid tenantId, string name, Guid excludeId)
        {
            const string sql = @"
                SELECT EXISTS(
                    SELECT 1 FROM pass_product
                    WHERE tenant_id = @tenantId
                      AND is_active = true
                      AND lower(name) = lower(@name)
                      AND id <> @excludeId)";
            var result = await _db.Query<bool>(sql, new { tenantId, name, excludeId });
            return result.FirstOrDefault();
        }

        public async Task<PassProduct?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {SelectColumns} FROM pass_product WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            var result = await _db.Query<PassProduct>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<Guid> Create(PassProduct p)
        {
            const string sql = @"
                INSERT INTO pass_product (tenant_id, name, description, price_cents, is_active, sort_order, requires_waiver, rider_paid_service_charge_bps)
                VALUES (@TenantId, @Name, @Description, @PriceCents, @IsActive, @SortOrder, @RequiresWaiver, @RiderPaidServiceChargeBps)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, p);
            return result.First();
        }

        public async Task Update(PassProduct p)
        {
            const string sql = @"
                UPDATE pass_product
                SET name = @Name, description = @Description, price_cents = @PriceCents,
                    is_active = @IsActive, sort_order = @SortOrder,
                    requires_waiver = @RequiresWaiver,
                    rider_paid_service_charge_bps = @RiderPaidServiceChargeBps
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, p);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM pass_product WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task UpdateSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            const string sql = @"
                UPDATE pass_product AS p
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
    }
}
