using Services.Helpers.Interfaces;
using Services.Repositories.Data.ConcessionData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class ConcessionRepository : IConcessionRepository
    {
        private const string ProductCols = @"
            id, tenant_id AS TenantId, name, description, category,
            price_cents AS PriceCents, image_url AS ImageUrl,
            is_active AS IsActive, sort_order AS SortOrder,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string VariantCols = @"
            id, product_id AS ProductId, size, color, price_cents AS PriceCents,
            image_url AS ImageUrl, inventory, is_active AS IsActive,
            sort_order AS SortOrder, created_at AS CreatedAt";

        private const string SaleCols = @"
            id, tenant_id AS TenantId, status, subtotal_cents AS SubtotalCents,
            total_cents AS TotalCents, stripe_payment_intent_id AS StripePaymentIntentId,
            sold_by_user_id AS SoldByUserId, created_at AS CreatedAt, paid_at AS PaidAt";

        private readonly IDbHelper _db;

        public ConcessionRepository(IDbHelper db) => _db = db;

        // ── Products ──────────────────────────────────────────────────────────────
        public async Task<List<ConcessionProduct>> ListProducts(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? "AND is_active = true" : "";
            var sql = $@"
                SELECT {ProductCols}
                FROM concession_product
                WHERE tenant_id = @tenantId {filter}
                ORDER BY sort_order, LOWER(name)";
            return (await _db.Query<ConcessionProduct>(sql, new { tenantId })).ToList();
        }

        public async Task<ConcessionProduct?> GetProduct(Guid id, Guid tenantId)
        {
            var sql = $@"SELECT {ProductCols} FROM concession_product
                        WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ConcessionProduct>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateProduct(ConcessionProduct p)
        {
            const string sql = @"
                INSERT INTO concession_product
                    (tenant_id, name, description, category, price_cents, image_url, is_active, sort_order)
                VALUES (@TenantId, @Name, @Description, @Category, @PriceCents, @ImageUrl, @IsActive, @SortOrder)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        public async Task UpdateProduct(ConcessionProduct p)
        {
            const string sql = @"
                UPDATE concession_product SET
                    name = @Name, description = @Description, category = @Category,
                    price_cents = @PriceCents, image_url = @ImageUrl,
                    is_active = @IsActive, sort_order = @SortOrder, updated_at = now()
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, p);
        }

        public async Task DeleteProduct(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM concession_product WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task UpdateProductSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            const string sql = @"
                UPDATE concession_product AS p
                SET sort_order = data.sort_order
                FROM (SELECT unnest(@ids::uuid[]) AS id, unnest(@orders::int[]) AS sort_order) AS data
                WHERE p.id = data.id AND p.tenant_id = @tenantId";
            await _db.Execute(sql, new { tenantId, ids = ids.ToArray(), orders = sortOrders.ToArray() });
        }

        // ── Variants ──────────────────────────────────────────────────────────────
        public async Task<List<ConcessionVariant>> ListVariants(Guid productId)
        {
            var sql = $@"SELECT {VariantCols} FROM concession_variant
                        WHERE product_id = @productId ORDER BY sort_order, id";
            return (await _db.Query<ConcessionVariant>(sql, new { productId })).ToList();
        }

        public async Task<Dictionary<Guid, List<ConcessionVariant>>> ListVariantsForProducts(IEnumerable<Guid> productIds)
        {
            var ids = productIds.ToArray();
            if (ids.Length == 0) return new();
            var sql = $@"SELECT {VariantCols} FROM concession_variant
                        WHERE product_id = ANY(@ids) ORDER BY sort_order, id";
            var rows = await _db.Query<ConcessionVariant>(sql, new { ids });
            return rows.GroupBy(v => v.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        }

        public async Task<ConcessionVariant?> GetVariant(Guid id)
        {
            var sql = $@"SELECT {VariantCols} FROM concession_variant WHERE id = @id LIMIT 1";
            return (await _db.Query<ConcessionVariant>(sql, new { id })).FirstOrDefault();
        }

        public async Task<Guid> CreateVariant(ConcessionVariant v)
        {
            const string sql = @"
                INSERT INTO concession_variant
                    (product_id, size, color, price_cents, image_url, inventory, is_active, sort_order)
                VALUES (@ProductId, @Size, @Color, @PriceCents, @ImageUrl, @Inventory, @IsActive, @SortOrder)
                RETURNING id";
            return (await _db.Query<Guid>(sql, v)).First();
        }

        public async Task UpdateVariant(ConcessionVariant v)
        {
            const string sql = @"
                UPDATE concession_variant SET
                    size = @Size, color = @Color, price_cents = @PriceCents, image_url = @ImageUrl,
                    inventory = @Inventory, is_active = @IsActive, sort_order = @SortOrder
                WHERE id = @Id";
            await _db.Execute(sql, v);
        }

        public async Task DeleteVariant(Guid id)
        {
            await _db.Execute("DELETE FROM concession_variant WHERE id = @id", new { id });
        }

        // ── Sold counts ─────────────────────────────────────────────────────────────
        public async Task<Dictionary<Guid, int>> SumSoldVariants(IEnumerable<Guid> variantIds)
        {
            var ids = variantIds.ToArray();
            if (ids.Length == 0) return new();
            const string sql = @"
                SELECT l.variant_id AS VariantId, COALESCE(SUM(l.quantity), 0)::int AS Sold
                FROM concession_sale_line l
                JOIN concession_sale s ON s.id = l.sale_id
                WHERE l.variant_id = ANY(@ids) AND s.status IN ('pending', 'paid')
                GROUP BY l.variant_id";
            var rows = await _db.Query<(Guid VariantId, int Sold)>(sql, new { ids });
            return rows.ToDictionary(r => r.VariantId, r => r.Sold);
        }

        public async Task<int> SumSoldVariant(Guid variantId)
        {
            const string sql = @"
                SELECT COALESCE(SUM(l.quantity), 0)::int
                FROM concession_sale_line l
                JOIN concession_sale s ON s.id = l.sale_id
                WHERE l.variant_id = @variantId AND s.status IN ('pending', 'paid')";
            return (await _db.Query<int>(sql, new { variantId })).FirstOrDefault();
        }

        // ── Sales ─────────────────────────────────────────────────────────────────
        public async Task<Guid> CreateSale(ConcessionSale sale)
        {
            const string sql = @"
                INSERT INTO concession_sale
                    (tenant_id, status, subtotal_cents, total_cents, stripe_payment_intent_id, sold_by_user_id)
                VALUES (@TenantId, @Status, @SubtotalCents, @TotalCents, @StripePaymentIntentId, @SoldByUserId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, sale)).First();
        }

        public async Task CreateSaleLines(Guid saleId, IEnumerable<ConcessionSaleLine> lines)
        {
            const string sql = @"
                INSERT INTO concession_sale_line
                    (sale_id, product_id, variant_id, name_snapshot, variant_label, unit_price_cents, quantity, line_total_cents)
                VALUES (@SaleId, @ProductId, @VariantId, @NameSnapshot, @VariantLabel, @UnitPriceCents, @Quantity, @LineTotalCents)";
            foreach (var line in lines)
            {
                line.SaleId = saleId;
                await _db.Execute(sql, line);
            }
        }

        public async Task SetSalePaymentIntentId(Guid saleId, string paymentIntentId)
        {
            const string sql = "UPDATE concession_sale SET stripe_payment_intent_id = @paymentIntentId WHERE id = @saleId";
            await _db.Execute(sql, new { saleId, paymentIntentId });
        }

        public async Task<ConcessionSale?> GetSaleByPaymentIntentId(string paymentIntentId)
        {
            var sql = $@"SELECT {SaleCols} FROM concession_sale
                        WHERE stripe_payment_intent_id = @paymentIntentId LIMIT 1";
            return (await _db.Query<ConcessionSale>(sql, new { paymentIntentId })).FirstOrDefault();
        }

        public async Task<ConcessionSale?> GetSale(Guid id, Guid tenantId)
        {
            var sql = $@"SELECT {SaleCols} FROM concession_sale
                        WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ConcessionSale>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task MarkSalePaid(Guid saleId)
        {
            const string sql = @"UPDATE concession_sale SET status = 'paid', paid_at = now()
                                 WHERE id = @saleId AND status = 'pending'";
            await _db.Execute(sql, new { saleId });
        }

        public async Task MarkSaleFailed(Guid saleId)
        {
            const string sql = @"UPDATE concession_sale SET status = 'failed'
                                 WHERE id = @saleId AND status = 'pending'";
            await _db.Execute(sql, new { saleId });
        }
    }
}
