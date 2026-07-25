using Services.Helpers.Interfaces;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class BikeShopRepository : IBikeShopRepository
    {
        private readonly IDbHelper _db;
        public BikeShopRepository(IDbHelper db) => _db = db;

        // ── Column projections ────────────────────────────────────────────────────
        private const string CategoryCols = @"
            id, tenant_id AS TenantId, name, parent_id AS ParentId,
            sort_order AS SortOrder, is_active AS IsActive,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string SupplierCols = @"
            id, tenant_id AS TenantId, name, contact_name AS ContactName,
            email, phone, notes, is_active AS IsActive,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string ProductCols = @"
            id, tenant_id AS TenantId, category_id AS CategoryId, supplier_id AS SupplierId,
            name, description, brand, image_url AS ImageUrl,
            is_sellable AS IsSellable, is_published AS IsPublished, is_rentable AS IsRentable,
            is_active AS IsActive, sort_order AS SortOrder,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string ProductImageCols = @"
            id, tenant_id AS TenantId, product_id AS ProductId, image_url AS ImageUrl,
            caption, sort_order AS SortOrder, created_at AS CreatedAt";

        // AvailableCount resolves per tracking kind: cached count for pool, live available-item
        // count for serialized. Used everywhere a variant is read so callers never special-case it.
        private const string VariantCols = @"
            v.id, v.tenant_id AS TenantId, v.product_id AS ProductId,
            v.sku, v.barcode, v.size, v.color, v.gender,
            v.sale_price_cents AS SalePriceCents, v.msrp_cents AS MsrpCents, v.daily_rate_cents AS DailyRateCents,
            v.deposit_cents AS DepositCents, v.cost_cents AS CostCents, v.mpn AS Mpn,
            v.tracking_kind AS TrackingKind, v.stock_on_hand AS StockOnHand,
            v.low_stock_threshold AS LowStockThreshold, v.low_stock_notified_at AS LowStockNotifiedAt,
            v.reorder_point AS ReorderPoint, v.reorder_level AS ReorderLevel, v.vendor_part_number AS VendorPartNumber,
            v.is_active AS IsActive, v.created_at AS CreatedAt, v.updated_at AS UpdatedAt,
            CASE WHEN v.tracking_kind = 'serialized'
                 THEN (SELECT count(*) FROM shop_item i WHERE i.variant_id = v.id AND i.status = 'available')::int
                 ELSE v.stock_on_hand END AS AvailableCount";

        private const string ItemCols = @"
            id, tenant_id AS TenantId, variant_id AS VariantId, label, serial, notes,
            status, acquired_cost_cents AS AcquiredCostCents,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string MovementCols = @"
            id, tenant_id AS TenantId, variant_id AS VariantId, item_id AS ItemId,
            delta, reason, reference_kind AS ReferenceKind, reference_id AS ReferenceId,
            unit_cost_cents AS UnitCostCents, note, created_by_user_id AS CreatedByUserId,
            created_at AS CreatedAt";

        private const string PoCols = @"
            id, tenant_id AS TenantId, supplier_id AS SupplierId, reference, status, notes,
            ordered_at AS OrderedAt, expected_at AS ExpectedAt, received_at AS ReceivedAt,
            created_by_user_id AS CreatedByUserId, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string PoLineCols = @"
            id, po_id AS PoId, variant_id AS VariantId,
            quantity_ordered AS QuantityOrdered, quantity_received AS QuantityReceived,
            unit_cost_cents AS UnitCostCents, created_at AS CreatedAt";

        // ── Categories ────────────────────────────────────────────────────────────
        public async Task<List<ShopCategory>> ListCategories(Guid tenantId, bool activeOnly)
        {
            var sql = $@"SELECT {CategoryCols} FROM shop_category
                        WHERE tenant_id = @tenantId {(activeOnly ? "AND is_active = true" : "")}
                        ORDER BY sort_order, name";
            return (await _db.Query<ShopCategory>(sql, new { tenantId })).ToList();
        }

        public async Task<Guid> CreateCategory(ShopCategory c)
        {
            const string sql = @"
                INSERT INTO shop_category (tenant_id, name, parent_id, sort_order, is_active)
                VALUES (@TenantId, @Name, @ParentId, @SortOrder, @IsActive)
                RETURNING id";
            return (await _db.Query<Guid>(sql, c)).First();
        }

        public Task<int> UpdateCategory(ShopCategory c) => _db.Execute(@"
            UPDATE shop_category SET name = @Name, parent_id = @ParentId, sort_order = @SortOrder,
                is_active = @IsActive, updated_at = now()
            WHERE id = @Id AND tenant_id = @TenantId", c);

        public Task<int> DeleteCategory(Guid id, Guid tenantId) =>
            _db.Execute("DELETE FROM shop_category WHERE id = @id AND tenant_id = @tenantId", new { id, tenantId });

        // ── Suppliers ─────────────────────────────────────────────────────────────
        public async Task<List<ShopSupplier>> ListSuppliers(Guid tenantId, bool activeOnly)
        {
            var sql = $@"SELECT {SupplierCols} FROM shop_supplier
                        WHERE tenant_id = @tenantId {(activeOnly ? "AND is_active = true" : "")}
                        ORDER BY name";
            return (await _db.Query<ShopSupplier>(sql, new { tenantId })).ToList();
        }

        public async Task<Guid> CreateSupplier(ShopSupplier s)
        {
            const string sql = @"
                INSERT INTO shop_supplier (tenant_id, name, contact_name, email, phone, notes, is_active)
                VALUES (@TenantId, @Name, @ContactName, @Email, @Phone, @Notes, @IsActive)
                RETURNING id";
            return (await _db.Query<Guid>(sql, s)).First();
        }

        public Task<int> UpdateSupplier(ShopSupplier s) => _db.Execute(@"
            UPDATE shop_supplier SET name = @Name, contact_name = @ContactName, email = @Email,
                phone = @Phone, notes = @Notes, is_active = @IsActive, updated_at = now()
            WHERE id = @Id AND tenant_id = @TenantId", s);

        // ── Products + variants ───────────────────────────────────────────────────
        public async Task<List<ShopProductWithVariants>> ListProducts(Guid tenantId, bool activeOnly)
        {
            var pSql = $@"SELECT {ProductCols} FROM shop_product
                         WHERE tenant_id = @tenantId {(activeOnly ? "AND is_active = true" : "")}
                         ORDER BY sort_order, name";
            // Materialize the derived type straight from ProductCols. ShopProductWithVariants
            // adds no columns of its own, so Dapper fills every product field automatically —
            // and, unlike a hand-written copy, cannot silently drop one when a column is added.
            // (It did: the old Combine() omitted IsPublished, which defaults true, so unpublished
            // products stayed visible on the public storefront.)
            var products = (await _db.Query<ShopProductWithVariants>(pSql, new { tenantId })).ToList();
            if (products.Count == 0) return products;

            // One query for every variant across the returned products (no N+1).
            var ids = products.Select(p => p.Id).ToArray();
            var vSql = $@"SELECT {VariantCols} FROM shop_variant v
                         WHERE v.product_id = ANY(@ids) AND v.tenant_id = @tenantId
                         ORDER BY v.created_at";
            var variants = (await _db.Query<ShopVariantWithStock>(vSql, new { ids, tenantId })).ToList();
            var byProduct = variants.GroupBy(v => v.ProductId).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var p in products) p.Variants = byProduct.GetValueOrDefault(p.Id) ?? new();
            return products;
        }

        // The available-quantity expression, shared by the variant projection and the valuation
        // aggregate so a serialized bike is never counted as its (always zero) stock_on_hand.
        private const string AvailableQtyExpr = @"
            CASE WHEN v.tracking_kind = 'serialized'
                 THEN (SELECT count(*) FROM shop_item i WHERE i.variant_id = v.id AND i.status = 'available')::int
                 ELSE v.stock_on_hand END";

        // A pool variant at or below its threshold. Serialized units are present or not, so a
        // threshold is meaningless for them.
        private const string LowStockExistsExpr = @"
            EXISTS (SELECT 1 FROM shop_variant lv
                    WHERE lv.product_id = p.id AND lv.is_active = true
                      AND lv.tracking_kind = 'pool'
                      AND lv.low_stock_threshold IS NOT NULL
                      AND lv.stock_on_hand <= lv.low_stock_threshold)";

        public async Task<ShopCatalogPage> SearchProducts(
            Guid tenantId, ShopProductQuery q)
        {
            // Build the predicate once and reuse it for the count and the page, so the two can
            // never disagree about what "matching" means.
            var where = new List<string> { "p.tenant_id = @tenantId" };
            if (q.ActiveOnly) where.Add("p.is_active = true");
            if (q.CategoryId.HasValue) where.Add("p.category_id = @categoryId");
            if (q.SupplierId.HasValue) where.Add("p.supplier_id = @supplierId");
            if (q.Sellable == true) where.Add("p.is_sellable = true");
            if (q.Sellable == false) where.Add("p.is_sellable = false");
            if (q.Rentable == true) where.Add("p.is_rentable = true");
            if (q.Rentable == false) where.Add("p.is_rentable = false");

            // Type-or-scan: name/brand plus any variant's SKU or barcode. ILIKE so a scanned or
            // typed code matches regardless of the case it was cataloged in.
            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                where.Add(@"(
                    p.name ILIKE @search OR p.brand ILIKE @search
                    OR EXISTS (SELECT 1 FROM shop_variant sv
                               WHERE sv.product_id = p.id
                                 AND (sv.sku ILIKE @search OR sv.barcode ILIKE @search))
                )");
            }

            // Low stock is a pool-inventory concept: a serialized unit is either there or it isn't.
            if (q.LowStockOnly) where.Add(LowStockExistsExpr);

            var whereSql = string.Join(" AND ", where);
            var pageSize = Math.Clamp(q.PageSize, 1, 200);
            var offset = Math.Max(0, q.Page - 1) * pageSize;
            var args = new
            {
                tenantId,
                categoryId = q.CategoryId,
                supplierId = q.SupplierId,
                search = string.IsNullOrWhiteSpace(q.Search) ? null : $"%{q.Search.Trim()}%",
                limit = pageSize,
                offset,
            };

            // Match count and low-stock count in one pass over the same predicate.
            var countSql = $@"SELECT count(*)::int AS Total,
                                     count(*) FILTER (WHERE {LowStockExistsExpr})::int AS LowStockCount
                              FROM shop_product p WHERE {whereSql}";
            var counts = (await _db.Query<(int Total, int LowStockCount)>(countSql, args)).First();
            if (counts.Total == 0) return new ShopCatalogPage();

            // Header aggregates over the WHOLE filtered set, not just this page.
            var valueSql = $@"
                SELECT COALESCE(SUM(({AvailableQtyExpr}) * COALESCE(v.sale_price_cents, 0)), 0)::bigint AS StockRetailValueCents,
                       COALESCE(SUM(({AvailableQtyExpr}) * COALESCE(v.cost_cents, 0)), 0)::bigint AS StockCostValueCents
                FROM shop_variant v
                JOIN shop_product p ON p.id = v.product_id
                WHERE v.is_active = true AND {whereSql}";
            var values = (await _db.Query<(long Retail, long Cost)>(valueSql, args)).First();

            // Units ordered from a supplier but not yet received. Drafts ('open') are not on order yet.
            var poSql = $@"
                SELECT COALESCE(SUM(l.quantity_ordered - l.quantity_received), 0)::int
                FROM shop_po_line l
                JOIN shop_purchase_order po ON po.id = l.po_id
                JOIN shop_variant v ON v.id = l.variant_id
                JOIN shop_product p ON p.id = v.product_id
                WHERE po.tenant_id = @tenantId
                  AND po.status IN ('ordered','partial')
                  AND l.quantity_ordered > l.quantity_received
                  AND {whereSql}";
            var unitsOnPo = await _db.ExecuteScalar(poSql, args);

            var totals = new ShopCatalogTotals
            {
                StockRetailValueCents = values.Retail,
                StockCostValueCents = values.Cost,
                LowStockCount = counts.LowStockCount,
                UnitsOnPo = unitsOnPo,
            };

            var pSql = $@"SELECT {ProductCols} FROM shop_product p
                          WHERE {whereSql}
                          ORDER BY p.sort_order, p.name
                          LIMIT @limit OFFSET @offset";
            var products = (await _db.Query<ShopProductWithVariants>(pSql, args)).ToList();
            if (products.Count == 0) return new ShopCatalogPage { Total = counts.Total, Totals = totals };

            // Variants for this page only (no N+1, and no loading the whole catalog's variants).
            var ids = products.Select(p => p.Id).ToArray();
            var vSql = $@"SELECT {VariantCols} FROM shop_variant v
                          WHERE v.product_id = ANY(@ids) AND v.tenant_id = @tenantId
                          ORDER BY v.created_at";
            var variants = (await _db.Query<ShopVariantWithStock>(vSql, new { ids, tenantId })).ToList();
            var byProduct = variants.GroupBy(v => v.ProductId).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var p in products) p.Variants = byProduct.GetValueOrDefault(p.Id) ?? new();
            return new ShopCatalogPage
            {
                Rows = products,
                Total = counts.Total,
                Totals = totals,
            };
        }

        public async Task<ShopProductWithVariants?> GetProduct(Guid id, Guid tenantId)
        {
            var pSql = $"SELECT {ProductCols} FROM shop_product WHERE id = @id AND tenant_id = @tenantId";
            var product = (await _db.Query<ShopProductWithVariants>(pSql, new { id, tenantId })).FirstOrDefault();
            if (product is null) return null;
            var vSql = $@"SELECT {VariantCols} FROM shop_variant v
                         WHERE v.product_id = @id AND v.tenant_id = @tenantId ORDER BY v.created_at";
            product.Variants = (await _db.Query<ShopVariantWithStock>(vSql, new { id, tenantId })).ToList();
            return product;
        }

        // ── Product gallery (Script0230) ──────────────────────────────────────────
        public async Task<List<ShopProductImage>> ListProductImages(Guid productId, Guid tenantId) =>
            (await _db.Query<ShopProductImage>($@"
                SELECT {ProductImageCols} FROM shop_product_image
                WHERE product_id = @productId AND tenant_id = @tenantId
                ORDER BY sort_order, created_at", new { productId, tenantId })).ToList();

        public async Task<Dictionary<Guid, List<ShopProductImage>>> ListImagesForProducts(
            IEnumerable<Guid> productIds, Guid tenantId)
        {
            var ids = productIds.ToArray();
            if (ids.Length == 0) return new Dictionary<Guid, List<ShopProductImage>>();
            var rows = await _db.Query<ShopProductImage>($@"
                SELECT {ProductImageCols} FROM shop_product_image
                WHERE product_id = ANY(@ids) AND tenant_id = @tenantId
                ORDER BY sort_order, created_at", new { ids, tenantId });
            return rows.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        }

        public async Task<ShopProductImage?> GetProductImage(Guid imageId, Guid tenantId) =>
            (await _db.Query<ShopProductImage>($@"
                SELECT {ProductImageCols} FROM shop_product_image
                WHERE id = @imageId AND tenant_id = @tenantId", new { imageId, tenantId })).FirstOrDefault();

        public Task<int> CountProductImages(Guid productId, Guid tenantId) => _db.ExecuteScalar(
            "SELECT count(*) FROM shop_product_image WHERE product_id = @productId AND tenant_id = @tenantId",
            new { productId, tenantId });

        public async Task<ShopProductImage> AddProductImage(ShopProductImage image)
        {
            // Position is computed server-side (max + 10) so two admins uploading at the same
            // time can't both claim the same slot from a stale client-side count.
            var sql = $@"
                INSERT INTO shop_product_image (tenant_id, product_id, image_url, caption, sort_order)
                SELECT @TenantId, @ProductId, @ImageUrl, @Caption,
                       CASE WHEN @SortOrder > 0 THEN @SortOrder
                            ELSE COALESCE((SELECT max(sort_order) FROM shop_product_image
                                            WHERE product_id = @ProductId AND tenant_id = @TenantId), 0) + 10
                       END
                RETURNING {ProductImageCols}";
            return (await _db.Query<ShopProductImage>(sql, image)).First();
        }

        public Task<int> UpdateProductImageCaption(Guid imageId, Guid tenantId, string? caption) => _db.Execute(
            "UPDATE shop_product_image SET caption = @caption WHERE id = @imageId AND tenant_id = @tenantId",
            new { imageId, tenantId, caption });

        public Task<int> DeleteProductImage(Guid imageId, Guid tenantId) => _db.Execute(
            "DELETE FROM shop_product_image WHERE id = @imageId AND tenant_id = @tenantId",
            new { imageId, tenantId });

        public Task ReorderProductImages(Guid productId, Guid tenantId, IEnumerable<(Guid Id, int SortOrder)> order) =>
            // One transaction: a half-renumbered gallery is worse than a failed reorder.
            // product_id is re-asserted so an id from another product can't be renumbered in.
            _db.ExecuteBatch(order.Select(o => (
                @"UPDATE shop_product_image SET sort_order = @sortOrder
                  WHERE id = @id AND product_id = @productId AND tenant_id = @tenantId",
                (object?)new { id = o.Id, sortOrder = o.SortOrder, productId, tenantId })).ToList());

        public async Task<bool> IsImageUrlReferenced(Guid tenantId, string imageUrl, Guid exceptImageId) =>
            (await _db.Query<bool>(@"
                SELECT EXISTS (SELECT 1 FROM shop_product_image
                                WHERE tenant_id = @tenantId AND image_url = @imageUrl AND id <> @exceptImageId)
                    OR EXISTS (SELECT 1 FROM shop_product
                                WHERE tenant_id = @tenantId AND image_url = @imageUrl)",
                new { tenantId, imageUrl, exceptImageId })).First();

        public async Task<Guid> CreateProduct(ShopProduct p)
        {
            const string sql = @"
                INSERT INTO shop_product (tenant_id, category_id, supplier_id, name, description, brand,
                    image_url, is_sellable, is_published, is_rentable, is_active, sort_order)
                VALUES (@TenantId, @CategoryId, @SupplierId, @Name, @Description, @Brand,
                    @ImageUrl, @IsSellable, @IsPublished, @IsRentable, @IsActive, @SortOrder)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        public Task<int> UpdateProduct(ShopProduct p) => _db.Execute(@"
            UPDATE shop_product SET category_id = @CategoryId, supplier_id = @SupplierId, name = @Name,
                description = @Description, brand = @Brand, image_url = @ImageUrl,
                is_sellable = @IsSellable, is_published = @IsPublished, is_rentable = @IsRentable, is_active = @IsActive,
                sort_order = @SortOrder, updated_at = now()
            WHERE id = @Id AND tenant_id = @TenantId", p);

        public async Task<ShopVariant?> GetVariant(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {VariantCols} FROM shop_variant v WHERE v.id = @id AND v.tenant_id = @tenantId";
            return (await _db.Query<ShopVariantWithStock>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateVariant(ShopVariant v)
        {
            const string sql = @"
                INSERT INTO shop_variant (tenant_id, product_id, sku, barcode, size, color, gender,
                    sale_price_cents, msrp_cents, daily_rate_cents, deposit_cents, cost_cents, mpn, tracking_kind,
                    stock_on_hand, low_stock_threshold, reorder_point, reorder_level, vendor_part_number, is_active)
                VALUES (@TenantId, @ProductId, @Sku, @Barcode, @Size, @Color, @Gender,
                    @SalePriceCents, @MsrpCents, @DailyRateCents, @DepositCents, @CostCents, @Mpn, @TrackingKind,
                    @StockOnHand, @LowStockThreshold, @ReorderPoint, @ReorderLevel, @VendorPartNumber, @IsActive)
                RETURNING id";
            return (await _db.Query<Guid>(sql, v)).First();
        }

        // stock_on_hand is intentionally NOT updatable here: it moves only through AdjustPoolStock /
        // receiving, so the movement ledger stays the whole story. An admin editing a variant can't
        // silently rewrite the count.
        public Task<int> UpdateVariant(ShopVariant v) => _db.Execute(@"
            UPDATE shop_variant SET sku = @Sku, barcode = @Barcode, size = @Size, color = @Color,
                gender = @Gender, sale_price_cents = @SalePriceCents, msrp_cents = @MsrpCents, daily_rate_cents = @DailyRateCents,
                deposit_cents = @DepositCents, cost_cents = @CostCents, mpn = @Mpn, is_active = @IsActive,
                low_stock_threshold = @LowStockThreshold,
                reorder_point = @ReorderPoint, reorder_level = @ReorderLevel, vendor_part_number = @VendorPartNumber,
                -- Raising/clearing the threshold resets the alert episode so the new rule re-fires.
                low_stock_notified_at = CASE WHEN @LowStockThreshold IS NULL OR stock_on_hand > @LowStockThreshold
                                             THEN NULL ELSE low_stock_notified_at END,
                updated_at = now()
            WHERE id = @Id AND tenant_id = @TenantId", v);

        /// <summary>
        /// Atomically claims variants that just went low (at/below threshold, not yet alerted this
        /// episode), stamping their de-dupe timestamp so each low episode alerts once. Returns the
        /// claimed variants with product names for the notification body. Concession pattern.
        /// </summary>
        public async Task<List<(string ProductName, string? VariantLabel, int Available)>> MarkAndGetNewlyLowShopStock(Guid tenantId)
        {
            const string sql = @"
                WITH low AS (
                    SELECT v.id
                    FROM shop_variant v
                    WHERE v.tenant_id = @tenantId AND v.is_active = true AND v.low_stock_threshold IS NOT NULL
                      AND v.low_stock_notified_at IS NULL
                      AND (CASE WHEN v.tracking_kind = 'serialized'
                                THEN (SELECT count(*) FROM shop_item i WHERE i.variant_id = v.id AND i.status = 'available')
                                ELSE v.stock_on_hand END) <= v.low_stock_threshold
                    FOR UPDATE SKIP LOCKED
                ),
                claimed AS (
                    UPDATE shop_variant v SET low_stock_notified_at = now()
                    FROM low WHERE v.id = low.id
                    RETURNING v.id, v.product_id, v.size, v.color, v.gender, v.tracking_kind, v.stock_on_hand
                )
                SELECT p.name AS ProductName,
                       NULLIF(TRIM(BOTH ' / ' FROM COALESCE(c.size,'') ||
                           CASE WHEN c.color IS NOT NULL THEN ' / ' || c.color ELSE '' END), '') AS VariantLabel,
                       CASE WHEN c.tracking_kind = 'serialized'
                            THEN (SELECT count(*)::int FROM shop_item i WHERE i.variant_id = c.id AND i.status = 'available')
                            ELSE c.stock_on_hand END AS Available
                FROM claimed c JOIN shop_product p ON p.id = c.product_id";
            var rows = await _db.Query<LowStockRow>(sql, new { tenantId });
            return rows.Select(r => (r.ProductName, r.VariantLabel, r.Available)).ToList();
        }

        private class LowStockRow
        {
            public string ProductName { get; set; } = null!;
            public string? VariantLabel { get; set; }
            public int Available { get; set; }
        }

        // ── Serialized items ──────────────────────────────────────────────────────
        public async Task<List<ShopItem>> ListItems(Guid variantId, Guid tenantId)
        {
            var sql = $@"SELECT {ItemCols} FROM shop_item
                        WHERE variant_id = @variantId AND tenant_id = @tenantId
                        ORDER BY created_at";
            return (await _db.Query<ShopItem>(sql, new { variantId, tenantId })).ToList();
        }

        public async Task<ShopItem?> GetItem(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {ItemCols} FROM shop_item WHERE id = @id AND tenant_id = @tenantId";
            return (await _db.Query<ShopItem>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateItem(ShopItem i)
        {
            const string sql = @"
                INSERT INTO shop_item (tenant_id, variant_id, label, serial, notes, status, acquired_cost_cents)
                VALUES (@TenantId, @VariantId, @Label, @Serial, @Notes, @Status, @AcquiredCostCents)
                RETURNING id";
            return (await _db.Query<Guid>(sql, i)).First();
        }

        public Task<int> UpdateItem(ShopItem i) => _db.Execute(@"
            UPDATE shop_item SET label = @Label, serial = @Serial, notes = @Notes, status = @Status,
                acquired_cost_cents = @AcquiredCostCents, updated_at = now()
            WHERE id = @Id AND tenant_id = @TenantId", i);

        // ── Stock ─────────────────────────────────────────────────────────────────
        public async Task<int?> AdjustPoolStock(Guid variantId, Guid tenantId, int delta, string reason,
            string? note, Guid? byUserId, string? referenceKind = null, Guid? referenceId = null)
        {
            // One statement: the conditional UPDATE takes the row lock and re-checks the floor, so
            // concurrent adjustments serialize and can't drive stock negative; the movement is
            // inserted only when the UPDATE actually applied (FROM upd), so a rejected adjust leaves
            // no phantom movement. tracking_kind = 'pool' guard keeps this off serialized variants,
            // whose count is derived from items, not this column.
            const string sql = @"
                WITH upd AS (
                    UPDATE shop_variant
                    SET stock_on_hand = stock_on_hand + @delta, updated_at = now(),
                        low_stock_notified_at = CASE WHEN low_stock_threshold IS NOT NULL
                                                     AND stock_on_hand + @delta > low_stock_threshold
                                                     THEN NULL ELSE low_stock_notified_at END
                    WHERE id = @variantId AND tenant_id = @tenantId AND tracking_kind = 'pool'
                      AND stock_on_hand + @delta >= 0
                    RETURNING id, stock_on_hand
                ),
                mv AS (
                    INSERT INTO shop_stock_movement
                        (tenant_id, variant_id, delta, reason, reference_kind, reference_id, note, created_by_user_id)
                    SELECT @tenantId, id, @delta, @reason, @referenceKind, @referenceId, @note, @byUserId FROM upd
                    RETURNING id
                )
                SELECT stock_on_hand FROM upd";
            var rows = await _db.Query<int>(sql, new
            {
                variantId, tenantId, delta, reason, note, byUserId, referenceKind, referenceId,
            });
            return rows.Cast<int?>().FirstOrDefault();
        }

        public async Task<List<ShopStockMovement>> ListMovements(Guid variantId, Guid tenantId, int limit)
        {
            var sql = $@"SELECT {MovementCols} FROM shop_stock_movement
                        WHERE variant_id = @variantId AND tenant_id = @tenantId
                        ORDER BY created_at DESC LIMIT @limit";
            return (await _db.Query<ShopStockMovement>(sql, new { variantId, tenantId, limit })).ToList();
        }

        // ── Purchase orders ───────────────────────────────────────────────────────
        public async Task<List<ShopPurchaseOrder>> ListPurchaseOrders(Guid tenantId)
        {
            var sql = $"SELECT {PoCols} FROM shop_purchase_order WHERE tenant_id = @tenantId ORDER BY created_at DESC";
            return (await _db.Query<ShopPurchaseOrder>(sql, new { tenantId })).ToList();
        }

        public async Task<ShopPurchaseOrderWithLines?> GetPurchaseOrder(Guid id, Guid tenantId)
        {
            var poSql = $"SELECT {PoCols} FROM shop_purchase_order WHERE id = @id AND tenant_id = @tenantId";
            var po = (await _db.Query<ShopPurchaseOrder>(poSql, new { id, tenantId })).FirstOrDefault();
            if (po is null) return null;
            var lSql = $"SELECT {PoLineCols} FROM shop_po_line WHERE po_id = @id ORDER BY created_at";
            var lines = (await _db.Query<ShopPoLine>(lSql, new { id })).ToList();
            return new ShopPurchaseOrderWithLines
            {
                Id = po.Id, TenantId = po.TenantId, SupplierId = po.SupplierId, Reference = po.Reference,
                Status = po.Status, Notes = po.Notes, OrderedAt = po.OrderedAt, ExpectedAt = po.ExpectedAt,
                ReceivedAt = po.ReceivedAt, CreatedByUserId = po.CreatedByUserId, CreatedAt = po.CreatedAt,
                UpdatedAt = po.UpdatedAt, Lines = lines,
            };
        }

        public async Task<Guid> CreatePurchaseOrder(ShopPurchaseOrder po)
        {
            const string sql = @"
                INSERT INTO shop_purchase_order (tenant_id, supplier_id, reference, status, notes,
                    ordered_at, expected_at, created_by_user_id)
                VALUES (@TenantId, @SupplierId, @Reference, @Status, @Notes,
                    @OrderedAt, @ExpectedAt, @CreatedByUserId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, po)).First();
        }

        public Task<int> UpdatePurchaseOrder(ShopPurchaseOrder po) => _db.Execute(@"
            UPDATE shop_purchase_order SET supplier_id = @SupplierId, reference = @Reference,
                status = @Status, notes = @Notes, ordered_at = @OrderedAt, expected_at = @ExpectedAt,
                updated_at = now()
            WHERE id = @Id AND tenant_id = @TenantId", po);

        /// <summary>Pool variants sitting at or below their reorder point, with a suggested top-up
        /// quantity and the supplier to order from. Serialized units don't reorder by quantity, so
        /// they're excluded.</summary>
        public async Task<List<ShopReorderRow>> GetReorderWorklist(Guid tenantId)
        {
            var rows = (await _db.Query<ShopReorderRow>(@"
                SELECT v.id AS VariantId, p.id AS ProductId, p.name AS ProductName,
                       NULLIF(trim(concat_ws(' / ', v.size, v.color)), '') AS VariantLabel,
                       v.sku AS Sku, v.vendor_part_number AS VendorPartNumber,
                       p.supplier_id AS SupplierId, s.name AS SupplierName,
                       v.stock_on_hand AS Available,
                       v.reorder_point AS ReorderPoint, v.reorder_level AS ReorderLevel,
                       v.cost_cents AS CostCents
                FROM shop_variant v
                JOIN shop_product p ON p.id = v.product_id
                LEFT JOIN shop_supplier s ON s.id = p.supplier_id
                WHERE v.tenant_id = @tenantId AND v.is_active = true AND p.is_active = true
                  AND v.tracking_kind = 'pool'
                  AND v.reorder_point IS NOT NULL AND v.reorder_point > 0
                  AND v.stock_on_hand <= v.reorder_point
                ORDER BY s.name NULLS FIRST, p.name, v.sku",
                new { tenantId })).ToList();
            foreach (var r in rows)
                // Order up to the level when set, else one past the point; never less than 1.
                r.SuggestedQty = Math.Max(1, (r.ReorderLevel ?? r.ReorderPoint + 1) - r.Available);
            return rows;
        }

        /// <summary>Creates a purchase order and its lines in one transaction (used by the reorder
        /// worklist). The supplier and every variant are verified against the tenant first.</summary>
        public async Task<Guid?> CreatePurchaseOrderWithLines(Guid tenantId, Guid? supplierId, string? reference,
            DateTime? expectedAt, Guid? createdByUserId, IReadOnlyList<(Guid VariantId, int Qty, int? UnitCostCents)> lines)
        {
            if (lines.Count == 0) return null;
            // Supplier (if given) and all variants must belong to this tenant.
            if (supplierId is Guid sid && !(await _db.Query<int>(
                    "SELECT 1 FROM shop_supplier WHERE id = @sid AND tenant_id = @tenantId", new { sid, tenantId })).Any())
                return null;
            var variantIds = lines.Select(l => l.VariantId).Distinct().ToArray();
            var ownedCount = await _db.ExecuteScalar(
                "SELECT count(*)::int FROM shop_variant WHERE tenant_id = @tenantId AND id = ANY(@variantIds)",
                new { tenantId, variantIds });
            if (ownedCount != variantIds.Length) return null;

            var poId = Guid.NewGuid();
            var stmts = new List<(string Sql, object? Param)>
            {
                (@"INSERT INTO shop_purchase_order (id, tenant_id, supplier_id, reference, status, ordered_at, expected_at, created_by_user_id)
                   VALUES (@poId, @tenantId, @supplierId, @reference, 'open', now(), @expectedAt, @createdByUserId)",
                    new { poId, tenantId, supplierId, reference, expectedAt, createdByUserId }),
            };
            foreach (var l in lines)
                stmts.Add((@"INSERT INTO shop_po_line (po_id, variant_id, quantity_ordered, quantity_received, unit_cost_cents)
                            VALUES (@poId, @VariantId, @Qty, 0, @UnitCostCents)",
                    new { poId, l.VariantId, l.Qty, l.UnitCostCents }));
            await _db.ExecuteBatch(stmts);
            return poId;
        }

        public async Task<Guid> AddPurchaseOrderLine(ShopPoLine line, Guid tenantId)
        {
            // Guard the parent PO is in this tenant before attaching a line (po_line has no tenant_id
            // of its own; it inherits scope through the PO).
            const string sql = @"
                INSERT INTO shop_po_line (po_id, variant_id, quantity_ordered, quantity_received, unit_cost_cents)
                SELECT @PoId, @VariantId, @QuantityOrdered, 0, @UnitCostCents
                FROM shop_purchase_order po
                WHERE po.id = @PoId AND po.tenant_id = @tenantId
                RETURNING id";
            var rows = await _db.Query<Guid>(sql, new
            {
                line.PoId, line.VariantId, line.QuantityOrdered, line.UnitCostCents, tenantId,
            });
            return rows.FirstOrDefault();
        }

        public async Task<ShopPoLine?> GetPurchaseOrderLine(Guid lineId, Guid tenantId)
        {
            // Columns qualified with l. because of the PO join (which carries the tenant scope —
            // po_line has no tenant_id of its own).
            const string sql = @"
                SELECT l.id, l.po_id AS PoId, l.variant_id AS VariantId,
                       l.quantity_ordered AS QuantityOrdered, l.quantity_received AS QuantityReceived,
                       l.unit_cost_cents AS UnitCostCents, l.created_at AS CreatedAt
                FROM shop_po_line l
                JOIN shop_purchase_order po ON po.id = l.po_id AND po.tenant_id = @tenantId
                WHERE l.id = @lineId";
            return (await _db.Query<ShopPoLine>(sql, new { lineId, tenantId })).FirstOrDefault();
        }

        private class PoLineReceiveInfo
        {
            public Guid Id { get; set; }
            public Guid PoId { get; set; }
            public Guid VariantId { get; set; }
            public int QuantityOrdered { get; set; }
            public int QuantityReceived { get; set; }
            public int UnitCostCents { get; set; }
            public string TrackingKind { get; set; } = null!;
        }

        public async Task<bool> ReceivePurchaseOrderLine(Guid lineId, Guid tenantId, int quantity,
            IReadOnlyList<(string Label, string? Serial)>? serialUnits, Guid? byUserId)
        {
            if (quantity <= 0) return false;

            // Serialize receipts on this line so a concurrent receive can't both pass the
            // "<= ordered" guard and over-receive (read-validate-write across round trips).
            await using var gate = await _db.AcquireAdvisoryLock($"shop_po_line_receive:{lineId}");

            var info = (await _db.Query<PoLineReceiveInfo>(@"
                SELECT l.id, l.po_id AS PoId, l.variant_id AS VariantId,
                       l.quantity_ordered AS QuantityOrdered, l.quantity_received AS QuantityReceived,
                       l.unit_cost_cents AS UnitCostCents, v.tracking_kind AS TrackingKind
                FROM shop_po_line l
                JOIN shop_purchase_order po ON po.id = l.po_id AND po.tenant_id = @tenantId
                JOIN shop_variant v ON v.id = l.variant_id
                WHERE l.id = @lineId", new { lineId, tenantId })).FirstOrDefault();
            if (info is null) return false;
            if (info.QuantityReceived + quantity > info.QuantityOrdered) return false;

            var serialized = info.TrackingKind == "serialized";
            if (serialized && (serialUnits is null || serialUnits.Count != quantity)) return false;

            var stmts = new List<(string Sql, object? Param)>
            {
                // 1. Bump received count.
                ("UPDATE shop_po_line SET quantity_received = quantity_received + @quantity WHERE id = @lineId",
                    new { lineId, quantity }),
                // 2. Snapshot last cost onto the variant.
                ("UPDATE shop_variant SET cost_cents = @unitCost, updated_at = now() WHERE id = @variantId AND tenant_id = @tenantId",
                    new { unitCost = info.UnitCostCents, variantId = info.VariantId, tenantId }),
            };

            if (!serialized)
            {
                // 3a. Pool: bump the cached count and write one 'receive' movement, atomically.
                stmts.Add((@"
                    WITH upd AS (
                        UPDATE shop_variant SET stock_on_hand = stock_on_hand + @quantity, updated_at = now(),
                            low_stock_notified_at = CASE WHEN low_stock_threshold IS NOT NULL
                                                         AND stock_on_hand + @quantity > low_stock_threshold
                                                         THEN NULL ELSE low_stock_notified_at END
                        WHERE id = @variantId AND tenant_id = @tenantId RETURNING id
                    )
                    INSERT INTO shop_stock_movement
                        (tenant_id, variant_id, delta, reason, reference_kind, reference_id, unit_cost_cents, created_by_user_id)
                    SELECT @tenantId, id, @quantity, 'receive', 'purchase_order', @poId, @unitCost, @byUserId FROM upd",
                    new { variantId = info.VariantId, tenantId, quantity, poId = info.PoId, unitCost = info.UnitCostCents, byUserId }));
            }
            else
            {
                // 3b. Serialized: mint one item + one movement per unit.
                foreach (var (label, serial) in serialUnits!)
                {
                    var itemId = Guid.NewGuid();
                    stmts.Add(("INSERT INTO shop_item (id, tenant_id, variant_id, label, serial, status, acquired_cost_cents) " +
                               "VALUES (@itemId, @tenantId, @variantId, @label, @serial, 'available', @unitCost)",
                        new { itemId, tenantId, variantId = info.VariantId, label, serial, unitCost = info.UnitCostCents }));
                    stmts.Add(("INSERT INTO shop_stock_movement " +
                               "(tenant_id, variant_id, item_id, delta, reason, reference_kind, reference_id, unit_cost_cents, created_by_user_id) " +
                               "VALUES (@tenantId, @variantId, @itemId, 1, 'receive', 'purchase_order', @poId, @unitCost, @byUserId)",
                        new { tenantId, variantId = info.VariantId, itemId, poId = info.PoId, unitCost = info.UnitCostCents, byUserId }));
                }
            }

            // 4. Roll the PO status: fully received when no line is short, else partial. Runs after
            // the line bump in the same transaction, so the EXISTS sees the new received counts.
            stmts.Add((@"
                UPDATE shop_purchase_order po SET
                    status = CASE WHEN NOT EXISTS (
                                 SELECT 1 FROM shop_po_line l WHERE l.po_id = po.id AND l.quantity_received < l.quantity_ordered)
                             THEN 'received' ELSE 'partial' END,
                    received_at = CASE WHEN NOT EXISTS (
                                 SELECT 1 FROM shop_po_line l WHERE l.po_id = po.id AND l.quantity_received < l.quantity_ordered)
                             THEN now() ELSE received_at END,
                    updated_at = now()
                WHERE po.id = @poId AND po.tenant_id = @tenantId AND po.status <> 'cancelled'",
                new { poId = info.PoId, tenantId }));

            await _db.ExecuteBatch(stmts);
            return true;
        }

        // ── Tax categories ──────────────────────────────────────────────────────────
        private const string TaxCols = @"
            id, tenant_id AS TenantId, name, rate_bps AS RateBps, is_default AS IsDefault,
            sort_order AS SortOrder, is_active AS IsActive, created_at AS CreatedAt";

        public async Task<List<ShopTaxCategory>> ListTaxCategories(Guid tenantId, bool activeOnly)
        {
            var sql = $@"SELECT {TaxCols} FROM shop_tax_category
                        WHERE tenant_id = @tenantId {(activeOnly ? "AND is_active = true" : "")}
                        ORDER BY sort_order, name";
            return (await _db.Query<ShopTaxCategory>(sql, new { tenantId })).ToList();
        }

        public async Task<Guid> CreateTaxCategory(ShopTaxCategory c)
        {
            // Only one default per tenant (the unique partial index enforces it), so clear the
            // current default before setting a new one, or the insert would collide.
            if (c.IsDefault) await ClearDefaultTaxCategory(c.TenantId);
            const string sql = @"
                INSERT INTO shop_tax_category (tenant_id, name, rate_bps, is_default, sort_order, is_active)
                VALUES (@TenantId, @Name, @RateBps, @IsDefault, @SortOrder, @IsActive)
                RETURNING id";
            return (await _db.Query<Guid>(sql, c)).First();
        }

        public async Task<int> UpdateTaxCategory(ShopTaxCategory c)
        {
            if (c.IsDefault) await ClearDefaultTaxCategory(c.TenantId, exceptId: c.Id);
            return await _db.Execute(@"
                UPDATE shop_tax_category SET name = @Name, rate_bps = @RateBps, is_default = @IsDefault,
                    sort_order = @SortOrder, is_active = @IsActive
                WHERE id = @Id AND tenant_id = @TenantId", c);
        }

        private Task ClearDefaultTaxCategory(Guid tenantId, Guid? exceptId = null) => _db.Execute(
            "UPDATE shop_tax_category SET is_default = false WHERE tenant_id = @tenantId AND is_default = true AND (@exceptId IS NULL OR id <> @exceptId)",
            new { tenantId, exceptId });

        // ── Sales ─────────────────────────────────────────────────────────────────
        /// <summary>Paid online orders still on the shelf waiting for their customer.</summary>
        private const string AwaitingPickupExpr =
            "(s.order_channel = 'online' AND s.status = 'paid' AND s.picked_up_at IS NULL)";

        /// <summary>Dapper target for the sales totals row.</summary>
        private class ShopSaleTotalsRow
        {
            public int Total { get; set; }
            public long PaidCents { get; set; }
            public long RefundedCents { get; set; }
            public long TaxCents { get; set; }
            public int PaidCount { get; set; }
            public int RefundedCount { get; set; }
        }

        private const string SaleCols = @"
            id, tenant_id AS TenantId, buyer_user_id AS BuyerUserId, buyer_email AS BuyerEmail,
            buyer_name AS BuyerName, status, subtotal_cents AS SubtotalCents, discount_cents AS DiscountCents,
            tax_cents AS TaxCents, tip_cents AS TipCents, total_cents AS TotalCents,
            prices_include_tax AS PricesIncludeTax, payment_method AS PaymentMethod,
            stripe_payment_intent_id AS StripePaymentIntentId, stripe_connected_account_id AS StripeConnectedAccountId,
            order_number AS OrderNumber, sold_by_user_id AS SoldByUserId, work_order_id AS WorkOrderId,
            deposit_applied_cents AS DepositAppliedCents, credit_applied_cents AS CreditAppliedCents,
            credit_account_id AS CreditAccountId, gift_card_applied_cents AS GiftCardAppliedCents,
            gift_card_id AS GiftCardId, receipt_token AS ReceiptToken,
            order_channel AS OrderChannel, picked_up_at AS PickedUpAt,
            refunded_at AS RefundedAt, refund_note AS RefundNote, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string SaleLineCols = @"
            id, sale_id AS SaleId, variant_id AS VariantId, item_id AS ItemId, quantity,
            name_snapshot AS NameSnapshot, variant_label AS VariantLabel, unit_price_cents AS UnitPriceCents,
            discount_cents AS DiscountCents, tax_cents AS TaxCents, tax_rate_bps AS TaxRateBps,
            unit_cost_cents_frozen AS UnitCostCentsFrozen, created_at AS CreatedAt";

        public async Task<(Guid Id, Guid ReceiptToken)> CreateSale(ShopSale sale, IEnumerable<ShopSaleLine> lines)
        {
            // Ids generated here so the sale and all its lines write in ONE transaction (ExecuteBatch)
            // — a line failure rolls the whole sale back rather than leaving an orphan header. Both
            // columns otherwise DB-default to gen_random_uuid(); an explicit value overrides that.
            var saleId = Guid.NewGuid();
            var receipt = Guid.NewGuid();
            var stmts = new List<(string Sql, object? Param)>
            {
                (@"INSERT INTO shop_sale (id, tenant_id, buyer_user_id, buyer_email, buyer_name, status,
                        subtotal_cents, discount_cents, tax_cents, tip_cents, total_cents, prices_include_tax,
                        payment_method, sold_by_user_id, work_order_id, deposit_applied_cents,
                        credit_applied_cents, credit_account_id, gift_card_applied_cents, gift_card_id,
                        order_channel, receipt_token)
                   VALUES (@id, @TenantId, @BuyerUserId, @BuyerEmail, @BuyerName, @Status,
                        @SubtotalCents, @DiscountCents, @TaxCents, @TipCents, @TotalCents, @PricesIncludeTax,
                        @PaymentMethod, @SoldByUserId, @WorkOrderId, @DepositAppliedCents,
                        @CreditAppliedCents, @CreditAccountId, @GiftCardAppliedCents, @GiftCardId,
                        @OrderChannel, @receipt)",
                    new
                    {
                        id = saleId, sale.TenantId, sale.BuyerUserId, sale.BuyerEmail, sale.BuyerName,
                        sale.Status, sale.SubtotalCents, sale.DiscountCents, sale.TaxCents, sale.TipCents,
                        sale.TotalCents, sale.PricesIncludeTax, sale.PaymentMethod, sale.SoldByUserId,
                        sale.WorkOrderId, sale.DepositAppliedCents, sale.CreditAppliedCents, sale.CreditAccountId,
                        sale.GiftCardAppliedCents, sale.GiftCardId, sale.OrderChannel, receipt,
                    }),
            };
            foreach (var l in lines)
            {
                stmts.Add((@"
                    INSERT INTO shop_sale_line (sale_id, variant_id, item_id, quantity, name_snapshot,
                        variant_label, unit_price_cents, discount_cents, tax_cents, tax_rate_bps,
                        unit_cost_cents_frozen)
                    VALUES (@saleId, @VariantId, @ItemId, @Quantity, @NameSnapshot, @VariantLabel,
                        @UnitPriceCents, @DiscountCents, @TaxCents, @TaxRateBps, @UnitCostCentsFrozen)",
                    new
                    {
                        saleId, l.VariantId, l.ItemId, l.Quantity, l.NameSnapshot, l.VariantLabel,
                        l.UnitPriceCents, l.DiscountCents, l.TaxCents, l.TaxRateBps, l.UnitCostCentsFrozen,
                    }));
            }
            await _db.ExecuteBatch(stmts);
            return (saleId, receipt);
        }

        public async Task<ShopSaleWithLines?> GetSale(Guid id, Guid tenantId)
        {
            // Dapper materializes the derived type from SaleCols directly; ShopSaleWithLines adds
            // no columns of its own. The hand-written copy this replaces omitted OrderChannel and
            // PickedUpAt (both default to the counter/never-collected values), which silently
            // disabled the admin's whole online-pickup workflow.
            var sale = (await _db.Query<ShopSaleWithLines>($"SELECT {SaleCols} FROM shop_sale WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId })).FirstOrDefault();
            if (sale is null) return null;
            sale.Lines = (await _db.Query<ShopSaleLine>($"SELECT {SaleLineCols} FROM shop_sale_line WHERE sale_id = @id ORDER BY created_at",
                new { id })).ToList();
            return sale;
        }

        // Online order collected at the counter. Guarded so only a paid online sale flips, once.
        public async Task<bool> MarkSalePickedUp(Guid saleId, Guid tenantId) =>
            await _db.Execute(@"
                UPDATE shop_sale SET picked_up_at = now(), updated_at = now()
                WHERE id = @saleId AND tenant_id = @tenantId AND order_channel = 'online'
                  AND status = 'paid' AND picked_up_at IS NULL",
                new { saleId, tenantId }) > 0;

        public async Task<ShopSale?> GetSaleByPaymentIntentId(string paymentIntentId) =>
            (await _db.Query<ShopSale>($"SELECT {SaleCols} FROM shop_sale WHERE stripe_payment_intent_id = @paymentIntentId LIMIT 1",
                new { paymentIntentId })).FirstOrDefault();

        public Task SetSalePaymentIntent(Guid id, string paymentIntentId) => _db.Execute(
            "UPDATE shop_sale SET stripe_payment_intent_id = @paymentIntentId, updated_at = now() WHERE id = @id",
            new { id, paymentIntentId });

        public Task MarkSaleDirectCharge(Guid id, Guid tenantId, string connectedAccountId) => _db.Execute(@"
            UPDATE shop_sale SET stripe_connected_account_id = @connectedAccountId,
                payment_method = 'stripe_direct', updated_at = now()
            WHERE id = @id AND tenant_id = @tenantId", new { id, tenantId, connectedAccountId });

        public async Task<bool> TryMarkSalePaid(Guid id, Guid tenantId)
        {
            // Flip only from 'pending', and only the call that actually flips gets a row back — that
            // caller owns running depletion + ledger once, even if the webhook and reconciler race.
            var rows = await _db.Query<Guid>(
                "UPDATE shop_sale SET status = 'paid', updated_at = now() WHERE id = @id AND tenant_id = @tenantId AND status = 'pending' RETURNING id",
                new { id, tenantId });
            return rows.Any();
        }

        public Task MarkSaleFailed(Guid id) => _db.Execute(
            "UPDATE shop_sale SET status = 'failed', updated_at = now() WHERE id = @id AND status = 'pending'",
            new { id });

        public Task<int> MarkSaleRefunded(Guid id, Guid tenantId, string? note) => _db.Execute(@"
            UPDATE shop_sale SET status = 'refunded', refunded_at = now(), refund_note = @note, updated_at = now()
            WHERE id = @id AND tenant_id = @tenantId AND status = 'paid'", new { id, tenantId, note });

        public async Task DepleteForSale(Guid saleId, Guid tenantId, Guid? byUserId)
        {
            // A sale billed out of a work order must not deplete: its parts were consumed (with
            // 'repair_consume' movements) when they were added to the job, and its labor lines
            // aren't stock at all.
            var fromWorkOrder = (await _db.Query<int>(
                "SELECT 1 FROM shop_sale WHERE id = @saleId AND tenant_id = @tenantId AND work_order_id IS NOT NULL",
                new { saleId, tenantId })).Any();
            if (fromWorkOrder) return;

            var lines = (await _db.Query<ShopSaleLine>(
                "SELECT variant_id AS VariantId, item_id AS ItemId, quantity FROM shop_sale_line WHERE sale_id = @saleId",
                new { saleId })).ToList();
            if (lines.Count == 0) return;

            var stmts = new List<(string Sql, object? Param)>();
            foreach (var l in lines)
            {
                if (l.VariantId is null) continue;   // labor line (work-order sales never get here, but be safe)
                if (l.ItemId is null)
                {
                    // Pool: decrement the cached count and record the movement. No floor guard — the
                    // sale already happened; a rare oversell race surfaces as negative stock (a signal)
                    // rather than blocking a paid sale's depletion.
                    stmts.Add((@"
                        WITH upd AS (
                            UPDATE shop_variant SET stock_on_hand = stock_on_hand - @qty, updated_at = now()
                            WHERE id = @variantId AND tenant_id = @tenantId RETURNING id
                        )
                        INSERT INTO shop_stock_movement
                            (tenant_id, variant_id, delta, reason, reference_kind, reference_id, created_by_user_id)
                        SELECT @tenantId, id, -@qty, 'sale', 'shop_sale', @saleId, @byUserId FROM upd",
                        new { variantId = l.VariantId, tenantId, qty = l.Quantity, saleId, byUserId }));
                }
                else
                {
                    // Serialized: mark the specific unit sold (idempotent guard status <> 'sold') and
                    // record a -1 movement against it.
                    stmts.Add(("UPDATE shop_item SET status = 'sold', updated_at = now() WHERE id = @itemId AND tenant_id = @tenantId AND status <> 'sold'",
                        new { itemId = l.ItemId, tenantId }));
                    stmts.Add((@"
                        INSERT INTO shop_stock_movement
                            (tenant_id, variant_id, item_id, delta, reason, reference_kind, reference_id, created_by_user_id)
                        VALUES (@tenantId, @variantId, @itemId, -1, 'sale', 'shop_sale', @saleId, @byUserId)",
                        new { tenantId, variantId = l.VariantId, itemId = l.ItemId, saleId, byUserId }));
                }
            }
            await _db.ExecuteBatch(stmts);
        }

        public async Task RestockForSale(Guid saleId, Guid tenantId, Guid? byUserId)
        {
            var lines = (await _db.Query<ShopSaleLine>(
                "SELECT variant_id AS VariantId, item_id AS ItemId, quantity FROM shop_sale_line WHERE sale_id = @saleId",
                new { saleId })).ToList();
            if (lines.Count == 0) return;

            var stmts = new List<(string Sql, object? Param)>();
            foreach (var l in lines)
            {
                if (l.VariantId is null) continue;   // labor line — nothing physical to restock
                if (l.ItemId is null)
                {
                    stmts.Add((@"
                        WITH upd AS (
                            UPDATE shop_variant SET stock_on_hand = stock_on_hand + @qty, updated_at = now(),
                                low_stock_notified_at = CASE WHEN low_stock_threshold IS NOT NULL
                                                             AND stock_on_hand + @qty > low_stock_threshold
                                                             THEN NULL ELSE low_stock_notified_at END
                            WHERE id = @variantId AND tenant_id = @tenantId RETURNING id
                        )
                        INSERT INTO shop_stock_movement
                            (tenant_id, variant_id, delta, reason, reference_kind, reference_id, created_by_user_id)
                        SELECT @tenantId, id, @qty, 'sale_return', 'shop_sale', @saleId, @byUserId FROM upd",
                        new { variantId = l.VariantId, tenantId, qty = l.Quantity, saleId, byUserId }));
                }
                else
                {
                    // Only a unit currently marked sold returns to stock (guard against a unit that
                    // was already re-sold or otherwise moved on since this sale).
                    stmts.Add(("UPDATE shop_item SET status = 'available', updated_at = now() WHERE id = @itemId AND tenant_id = @tenantId AND status = 'sold'",
                        new { itemId = l.ItemId, tenantId }));
                    stmts.Add((@"
                        INSERT INTO shop_stock_movement
                            (tenant_id, variant_id, item_id, delta, reason, reference_kind, reference_id, created_by_user_id)
                        VALUES (@tenantId, @variantId, @itemId, 1, 'sale_return', 'shop_sale', @saleId, @byUserId)",
                        new { tenantId, variantId = l.VariantId, itemId = l.ItemId, saleId, byUserId }));
                }
            }
            await _db.ExecuteBatch(stmts);
        }

        public async Task<List<ShopVariantSaleInfo>> GetVariantsForSale(IEnumerable<Guid> variantIds, Guid tenantId)
        {
            var ids = variantIds.Distinct().ToArray();
            if (ids.Length == 0) return new List<ShopVariantSaleInfo>();
            // Tax rate resolves to the product's category, else the tenant default, else 0. Available
            // is the live item count for serialized, the cached count for pool.
            const string sql = @"
                SELECT v.id, v.product_id AS ProductId, p.name AS ProductName,
                       v.size, v.color, v.gender, v.tracking_kind AS TrackingKind,
                       v.sale_price_cents AS SalePriceCents, v.cost_cents AS CostCents,
                       COALESCE(tc.rate_bps, dtc.rate_bps, 0) AS TaxRateBps,
                       CASE WHEN v.tracking_kind = 'serialized'
                            THEN (SELECT count(*) FROM shop_item i WHERE i.variant_id = v.id AND i.status = 'available')::int
                            ELSE v.stock_on_hand END AS Available
                FROM shop_variant v
                JOIN shop_product p ON p.id = v.product_id
                LEFT JOIN shop_tax_category tc ON tc.id = p.tax_category_id AND tc.tenant_id = v.tenant_id
                LEFT JOIN shop_tax_category dtc ON dtc.tenant_id = v.tenant_id AND dtc.is_default = true
                WHERE v.id = ANY(@ids) AND v.tenant_id = @tenantId AND v.is_active = true";
            return (await _db.Query<ShopVariantSaleInfo>(sql, new { ids, tenantId })).ToList();
        }

        public async Task<int> NextOrderNumber(Guid tenantId)
        {
            // Atomic per-tenant, per-local-day counter (same shape as concession_order_counter).
            const string sql = @"
                INSERT INTO shop_order_counter (tenant_id, business_date, last_number)
                SELECT @tenantId, (now() AT TIME ZONE COALESCE(NULLIF(t.timezone, ''), 'UTC'))::date, 1
                FROM tenant t WHERE t.id = @tenantId
                ON CONFLICT (tenant_id, business_date)
                DO UPDATE SET last_number = shop_order_counter.last_number + 1
                RETURNING last_number";
            return (await _db.Query<int>(sql, new { tenantId })).First();
        }

        public Task SetSaleOrderNumber(Guid id, int orderNumber) => _db.Execute(
            "UPDATE shop_sale SET order_number = @orderNumber, updated_at = now() WHERE id = @id",
            new { id, orderNumber });

        // ── Rentals ───────────────────────────────────────────────────────────────
        private const string RentalCols = @"
            id, tenant_id AS TenantId, renter_user_id AS RenterUserId, renter_name AS RenterName,
            renter_email AS RenterEmail, renter_phone AS RenterPhone,
            waiver_signature_id AS WaiverSignatureId, starts_at AS StartsAt, ends_at AS EndsAt,
            status, amount_cents AS AmountCents, tax_cents AS TaxCents, total_cents AS TotalCents,
            service_charge_cents AS ServiceChargeCents, riders_required AS RidersRequired,
            deposit_cents AS DepositCents, deposit_pi_id AS DepositPiId,
            deposit_captured_cents AS DepositCapturedCents, payment_method AS PaymentMethod,
            stripe_payment_intent_id AS StripePaymentIntentId,
            stripe_connected_account_id AS StripeConnectedAccountId,
            order_number AS OrderNumber, sold_by_user_id AS SoldByUserId, receipt_token AS ReceiptToken,
            checked_out_at AS CheckedOutAt, returned_at AS ReturnedAt, condition_notes AS ConditionNotes,
            event_id AS EventId, signature_request_token AS SignatureRequestToken,
            signature_request_sent_at AS SignatureRequestSentAt,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string RentalLineCols = @"
            id, rental_id AS RentalId, variant_id AS VariantId, item_id AS ItemId, quantity,
            name_snapshot AS NameSnapshot, variant_label AS VariantLabel,
            daily_rate_cents_frozen AS DailyRateCentsFrozen, deposit_cents_frozen AS DepositCentsFrozen,
            line_amount_cents AS LineAmountCents, created_at AS CreatedAt";

        // Half-open overlap: [a, b) collides with [s, e) iff a < e AND b > s. Reservation-holding
        // statuses are pending (payment in flight), paid, and out.
        private const string ActiveOverlapWhere = @"
            r.status IN ('pending','paid','out')
            AND r.starts_at < @endsAt AND r.ends_at > @startsAt";

        public async Task<int> GetPoolAvailability(Guid variantId, Guid tenantId, DateTime startsAt, DateTime endsAt)
        {
            // Fleet for a pool variant = what's on the shelf now + what's currently out on a rental
            // (checkout decremented stock, but those units still exist and come back). Availability
            // for the window is fleet minus every active reservation overlapping it.
            var sql = $@"
                WITH fleet AS (
                    SELECT v.stock_on_hand + COALESCE((
                        SELECT sum(l.quantity)::int FROM shop_rental_line l
                        JOIN shop_rental r ON r.id = l.rental_id
                        WHERE l.variant_id = v.id AND r.status = 'out'), 0) AS total
                    FROM shop_variant v
                    WHERE v.id = @variantId AND v.tenant_id = @tenantId AND v.tracking_kind = 'pool'
                ),
                reserved AS (
                    SELECT COALESCE(sum(l.quantity), 0)::int AS qty
                    FROM shop_rental_line l JOIN shop_rental r ON r.id = l.rental_id
                    WHERE l.variant_id = @variantId AND r.tenant_id = @tenantId AND {ActiveOverlapWhere}
                )
                SELECT GREATEST(fleet.total - reserved.qty, 0) FROM fleet, reserved";
            var rows = await _db.Query<int>(sql, new { variantId, tenantId, startsAt, endsAt });
            return rows.FirstOrDefault();   // no fleet row (wrong tenant / not pool) -> 0
        }

        public async Task<List<ShopItem>> GetFreeSerializedUnits(Guid variantId, Guid tenantId, DateTime startsAt, DateTime endsAt)
        {
            // The rentable fleet is units that are available now OR out on another rental (they
            // come back); sold/maintenance/retired are not bookable. A unit is free for the window
            // when no active reservation on it overlaps.
            var sql = $@"
                SELECT {ItemCols} FROM shop_item i
                WHERE i.variant_id = @variantId AND i.tenant_id = @tenantId
                  AND i.status IN ('available','rented_out')
                  AND NOT EXISTS (
                      SELECT 1 FROM shop_rental_line l JOIN shop_rental r ON r.id = l.rental_id
                      WHERE l.item_id = i.id AND {ActiveOverlapWhere})
                ORDER BY i.label";
            return (await _db.Query<ShopItem>(sql, new { variantId, tenantId, startsAt, endsAt })).ToList();
        }

        /// <summary>
        /// Whole rental fleet plus every reservation overlapping a window, for the Rental Board
        /// timeline. Three queries instead of one probe per variant: a timeline needs the
        /// individual reservations laid out in time across the whole fleet, which a per-variant
        /// scalar can't express.
        /// </summary>
        public async Task<ShopRentalBoard> GetRentalBoard(Guid tenantId, DateTime startsAt,
            DateTime endsAt, Guid? categoryId)
        {
            // Shared fleet predicate: the product and variant are live and rentable, and the
            // variant carries a rate (a rentable product whose variant has no daily rate can't be
            // booked, so it isn't fleet). Every join re-asserts tenant_id rather than trusting the
            // FK chain: one predicate per table is the rule in this codebase.
            const string fleetJoin = @"
                JOIN shop_variant  v ON v.id = i.variant_id AND v.tenant_id = @tenantId
                JOIN shop_product  p ON p.id = v.product_id AND p.tenant_id = @tenantId
                LEFT JOIN shop_category c ON c.id = p.category_id AND c.tenant_id = @tenantId";
            const string fleetWhere = @"
                v.is_active AND v.daily_rate_cents IS NOT NULL
                AND p.is_active AND p.is_rentable";

            // ── Serialized units: one row per physical bike. 'sold'/'retired' have left the
            // fleet; 'maintenance' stays visible so the board can show WHY a bike staff can see on
            // the rack isn't bookable, rather than silently omitting it.
            var serializedSql = $@"
                SELECT i.id AS Id, v.id AS VariantId, i.id AS ItemId,
                       v.tracking_kind AS TrackingKind,
                       p.id AS ProductId, p.name AS ProductName, p.brand AS Brand,
                       p.category_id AS CategoryId, c.name AS CategoryName,
                       v.size AS Size, v.color AS Color, v.gender AS Gender, v.sku AS Sku,
                       i.label AS UnitLabel, i.serial AS Serial, i.status AS ItemStatus,
                       1 AS Capacity,
                       v.daily_rate_cents AS DailyRateCents, v.deposit_cents AS DepositCents
                FROM shop_item i
                {fleetJoin}
                WHERE i.tenant_id = @tenantId
                  AND i.status IN ('available','rented_out','maintenance')
                  AND v.tracking_kind = 'serialized'
                  AND {fleetWhere}
                  AND (@categoryId::uuid IS NULL OR p.category_id = @categoryId)
                ORDER BY p.name, v.size NULLS FIRST, i.label";

            // ── Pool variants: one row per bucket. Capacity mirrors GetPoolAvailability's fleet
            // math exactly: stock on the shelf plus what's currently out on a rental, because
            // checkout decremented stock for units that still exist and come back.
            var poolSql = @"
                SELECT v.id AS Id, v.id AS VariantId, NULL::uuid AS ItemId,
                       v.tracking_kind AS TrackingKind,
                       p.id AS ProductId, p.name AS ProductName, p.brand AS Brand,
                       p.category_id AS CategoryId, c.name AS CategoryName,
                       v.size AS Size, v.color AS Color, v.gender AS Gender, v.sku AS Sku,
                       NULL::text AS UnitLabel, NULL::text AS Serial, NULL::text AS ItemStatus,
                       v.stock_on_hand + COALESCE((
                           SELECT sum(l.quantity)::int FROM shop_rental_line l
                           JOIN shop_rental r ON r.id = l.rental_id AND r.tenant_id = @tenantId
                           WHERE l.variant_id = v.id AND r.status = 'out'), 0) AS Capacity,
                       v.daily_rate_cents AS DailyRateCents, v.deposit_cents AS DepositCents
                FROM shop_variant v
                JOIN shop_product p ON p.id = v.product_id AND p.tenant_id = @tenantId
                LEFT JOIN shop_category c ON c.id = p.category_id AND c.tenant_id = @tenantId
                WHERE v.tenant_id = @tenantId
                  AND v.tracking_kind = 'pool'
                  AND v.is_active AND v.daily_rate_cents IS NOT NULL
                  AND p.is_active AND p.is_rentable
                  AND (@categoryId::uuid IS NULL OR p.category_id = @categoryId)
                ORDER BY p.name, v.size NULLS FIRST, v.color NULLS FIRST";

            // ── Reservations overlapping the window. Same half-open overlap and the same
            // reservation-holding statuses every other availability check uses, so a bar on the
            // board is exactly a booking that would block a new one.
            var segmentSql = $@"
                SELECT r.id AS RentalId, l.id AS LineId, l.variant_id AS VariantId,
                       l.item_id AS ItemId, l.quantity AS Quantity,
                       r.starts_at AS StartsAt, r.ends_at AS EndsAt, r.status AS Status,
                       r.renter_name AS RenterName, r.renter_email AS RenterEmail,
                       r.order_number AS OrderNumber, r.checked_out_at AS CheckedOutAt,
                       l.name_snapshot AS NameSnapshot, l.variant_label AS VariantLabel
                FROM shop_rental_line l
                JOIN shop_rental r ON r.id = l.rental_id
                WHERE r.tenant_id = @tenantId AND {ActiveOverlapWhere}
                ORDER BY r.starts_at";

            // Categories for the filter come from the UNFILTERED fleet: computing them from the
            // filtered set would leave the picker holding only the category already chosen.
            const string categorySql = @"
                SELECT DISTINCT c.id AS Id, c.name AS Name
                FROM shop_product p
                JOIN shop_variant v ON v.product_id = p.id AND v.tenant_id = @tenantId
                JOIN shop_category c ON c.id = p.category_id AND c.tenant_id = @tenantId
                WHERE p.tenant_id = @tenantId AND p.is_active AND p.is_rentable
                  AND v.is_active AND v.daily_rate_cents IS NOT NULL
                ORDER BY c.name";

            var args = new { tenantId, startsAt, endsAt, categoryId };
            var serialized = await _db.Query<ShopRentalBoardResource>(serializedSql, args);
            var pool = await _db.Query<ShopRentalBoardResource>(poolSql, args);
            var segments = await _db.Query<ShopRentalBoardSegment>(segmentSql, args);
            var categories = await _db.Query<ShopRentalBoardCategory>(categorySql, args);

            return new ShopRentalBoard
            {
                StartsAt = startsAt,
                EndsAt = endsAt,
                Resources = serialized.Concat(pool).ToList(),
                Segments = segments.ToList(),
                Categories = categories.ToList(),
            };
        }

        /// <summary>Links a captured waiver signature to a rental, so the checkout gate can see
        /// it without re-deriving who signed. Tenant-scoped; returns false if not this tenant's.</summary>
        /// <summary>Public signing page lookup. Tenant-scoped as well as token-scoped so a token
        /// can never surface a rental from another track.</summary>
        public async Task<ShopRentalWithLines?> GetRentalBySignatureToken(Guid token, Guid tenantId)
        {
            var rental = (await _db.Query<ShopRentalWithLines>(
                $"SELECT {RentalCols} FROM shop_rental WHERE signature_request_token = @token AND tenant_id = @tenantId",
                new { token, tenantId })).FirstOrDefault();
            if (rental is null) return null;
            rental.Lines = (await _db.Query<ShopRentalLine>(
                $"SELECT {RentalLineCols} FROM shop_rental_line WHERE rental_id = @id ORDER BY created_at",
                new { id = rental.Id })).ToList();
            return rental;
        }

        public Task MarkRentalSignatureRequestSent(Guid rentalId, Guid tenantId) => _db.Execute(
            "UPDATE shop_rental SET signature_request_sent_at = now(), updated_at = now() " +
            "WHERE id = @rentalId AND tenant_id = @tenantId",
            new { rentalId, tenantId });

        /// <summary>
        /// Records ANOTHER signed rider against the rental. Appends to shop_rental_waiver (the
        /// source of truth for "is everyone signed") and, for the first signer only, also stamps
        /// the legacy single waiver_signature_id so existing readers keep working. Signing twice
        /// with the same signature is a no-op rather than an error.
        /// </summary>
        public async Task<bool> AddRentalWaiverSignature(Guid rentalId, Guid tenantId, Guid signatureId)
        {
            // Tenant scope is enforced by the guarded UPDATE below; the insert is keyed off a
            // rental id the caller already resolved tenant-scoped.
            const string ins = @"
                INSERT INTO shop_rental_waiver (rental_id, signature_id)
                SELECT @rentalId, @signatureId
                WHERE EXISTS (SELECT 1 FROM shop_rental r WHERE r.id = @rentalId AND r.tenant_id = @tenantId)
                ON CONFLICT DO NOTHING";
            var inserted = await _db.Execute(ins, new { rentalId, tenantId, signatureId });

            const string primary = @"
                UPDATE shop_rental SET waiver_signature_id = @signatureId, updated_at = now()
                WHERE id = @rentalId AND tenant_id = @tenantId AND waiver_signature_id IS NULL";
            await _db.Execute(primary, new { rentalId, tenantId, signatureId });
            return inserted > 0;
        }

        /// <summary>How many distinct riders have signed for this rental.</summary>
        public async Task<int> CountRentalWaiverSignatures(Guid rentalId, Guid tenantId)
        {
            const string sql = @"
                SELECT count(*)::int FROM shop_rental_waiver w
                JOIN shop_rental r ON r.id = w.rental_id
                WHERE w.rental_id = @rentalId AND r.tenant_id = @tenantId";
            return await _db.ExecuteScalar(sql, new { rentalId, tenantId });
        }

        /// <summary>Who has signed, so the counter can see which riders are still outstanding.</summary>
        public async Task<List<RentalSignerInfo>> ListRentalWaiverSigners(Guid rentalId, Guid tenantId)
        {
            const string sql = @"
                -- The rider's own name lives in spectator_first/last_name (the columns the
                -- registrant-signing path writes). Fall back to signer_name for older rows.
                SELECT s.id AS SignatureId,
                       COALESCE(
                           NULLIF(TRIM(BOTH FROM COALESCE(s.spectator_first_name,'') || ' ' ||
                                                 COALESCE(s.spectator_last_name,'')), ''),
                           s.signer_name) AS RiderName,
                       s.signed_by_parent AS SignedByParent,
                       s.parent_name AS ParentName,
                       w.created_at AS SignedAtUtc
                FROM shop_rental_waiver w
                JOIN rider_waiver_signature s ON s.id = w.signature_id
                JOIN shop_rental r ON r.id = w.rental_id
                WHERE w.rental_id = @rentalId AND r.tenant_id = @tenantId
                ORDER BY w.created_at";
            return (await _db.Query<RentalSignerInfo>(sql, new { rentalId, tenantId })).ToList();
        }

        /// <summary>Sets how many riders must sign before the gear can leave.</summary>
        public async Task<bool> SetRentalRidersRequired(Guid rentalId, Guid tenantId, int ridersRequired)
        {
            const string sql = @"
                UPDATE shop_rental SET riders_required = @ridersRequired, updated_at = now()
                WHERE id = @rentalId AND tenant_id = @tenantId";
            return (await _db.Execute(sql, new { rentalId, tenantId, ridersRequired = Math.Max(1, ridersRequired) })) > 0;
        }

        // ── Inspections ───────────────────────────────────────────────────────────────────
        /// <summary>
        /// The tenant's default checklist, creating one on first use. New tenants never got a seed
        /// (the migration only backfilled existing ones), so this is where they get theirs rather
        /// than through a tenant-insert trigger for a feature most tenants never enable.
        /// </summary>
        public async Task<ShopInspectionTemplate> EnsureDefaultInspectionTemplate(Guid tenantId)
        {
            const string find = @"
                SELECT id, tenant_id AS TenantId, name, is_default AS IsDefault,
                       is_active AS IsActive, sort_order AS SortOrder
                FROM shop_inspection_template
                WHERE tenant_id = @tenantId AND is_default LIMIT 1";
            var existing = (await _db.Query<ShopInspectionTemplate>(find, new { tenantId })).FirstOrDefault();
            if (existing is not null) return existing;

            // The checklist has to match the machine: a motocross track checks fork seals and air
            // filters, a bike park checks spoke tension and bar tape. Seeding one list for both was
            // the bug Script0218 corrected; this keeps new tenants right.
            var isMx = (await _db.Query<string>(
                "SELECT tenant_type FROM tenant WHERE id = @tenantId", new { tenantId }))
                .FirstOrDefault() != "mountain_bike";

            const string ins = @"
                INSERT INTO shop_inspection_template (tenant_id, name, is_default)
                VALUES (@tenantId, @name, true)
                ON CONFLICT DO NOTHING
                RETURNING id";
            var newId = (await _db.Query<Guid>(ins, new
            {
                tenantId,
                name = isMx ? "Standard MX inspection" : "Standard MTB inspection",
            })).FirstOrDefault();
            if (newId == Guid.Empty)
                return (await _db.Query<ShopInspectionTemplate>(find, new { tenantId })).First();

            var rows = isMx ? DefaultMxChecklist : DefaultMtbChecklist;
            const string line = @"
                INSERT INTO shop_inspection_template_item (template_id, group_label, label, sort_order)
                VALUES (@newId, @g, @l, @o)";
            foreach (var (g, l, o) in rows)
                await _db.Execute(line, new { newId, g, l, o });

            return (await _db.Query<ShopInspectionTemplate>(find, new { tenantId })).First();
        }

        private static readonly (string G, string L, int O)[] DefaultMxChecklist =
        {
            ("Engine","Engine oil level and condition",10),("Engine","Oil filter",20),
            ("Engine","Air filter",30),("Engine","Coolant level",40),("Engine","Radiators and hoses",50),
            ("Engine","Spark plug",60),("Engine","Valve clearance",70),("Engine","Top-end hours",80),
            ("Engine","Exhaust / silencer packing",90),
            ("Drivetrain","Chain wear and tension",110),("Drivetrain","Front and rear sprockets",120),
            ("Drivetrain","Chain slider and rollers",130),("Drivetrain","Clutch free play and plates",140),
            ("Suspension","Fork seals and oil",210),("Suspension","Fork action",220),
            ("Suspension","Shock seals and action",230),("Suspension","Linkage bearings",240),
            ("Suspension","Swingarm bearings",250),("Suspension","Race sag",260),
            ("Brakes","Front and rear pads",310),("Brakes","Rotors",320),("Brakes","Fluid and lines",330),
            ("Wheels & tires","Tire wear and pressure",410),("Wheels & tires","Spoke tension",420),
            ("Wheels & tires","Rim condition",430),("Wheels & tires","Wheel bearings",440),
            ("Controls","Throttle action and cable",510),("Controls","Clutch lever and cable",520),
            ("Controls","Grips and bar mounts",530),
            ("Chassis","Frame and subframe",610),("Chassis","Steering head bearings",620),
            ("Chassis","Footpegs and shifter",630),("Chassis","Bolt torque",640),
        };

        private static readonly (string G, string L, int O)[] DefaultMtbChecklist =
        {
            ("Wheels & tires","Tire wear and pressure",10),("Wheels & tires","Wheel true",20),
            ("Wheels & tires","Spoke tension",30),("Wheels & tires","Hub bearings",40),
            ("Wheels & tires","Rim / rotor wear",50),
            ("Drivetrain","Chain wear",110),("Drivetrain","Cassette and chainrings",120),
            ("Drivetrain","Derailleur adjustment",130),("Drivetrain","Shift cables and housing",140),
            ("Drivetrain","Bottom bracket",150),
            ("Brakes","Pad wear",210),("Brakes","Lever feel / reach",220),("Brakes","Cables, hoses, fluid",230),
            ("Frame & fork","Frame condition",310),("Frame & fork","Headset",320),
            ("Frame & fork","Fork / suspension",330),("Frame & fork","Pivot bearings",340),
            ("Contact points","Saddle and seatpost",410),("Contact points","Handlebar and stem",420),
            ("Contact points","Grips / bar tape",430),("Contact points","Pedals",440),
            ("Safety","Quick releases / thru-axles",510),("Safety","Bolt torque",520),
            ("Safety","Lights and reflectors",530),
        };

        // ── Template editing ──────────────────────────────────────────────────────────────
        public async Task<List<ShopInspectionTemplate>> ListInspectionTemplates(Guid tenantId)
        {
            const string sql = @"
                SELECT id, tenant_id AS TenantId, name, is_default AS IsDefault,
                       is_active AS IsActive, sort_order AS SortOrder
                FROM shop_inspection_template
                WHERE tenant_id = @tenantId
                ORDER BY is_default DESC, sort_order, name";
            return (await _db.Query<ShopInspectionTemplate>(sql, new { tenantId })).ToList();
        }

        public async Task<ShopInspectionTemplate?> GetInspectionTemplate(Guid id, Guid tenantId)
        {
            const string sql = @"
                SELECT id, tenant_id AS TenantId, name, is_default AS IsDefault,
                       is_active AS IsActive, sort_order AS SortOrder
                FROM shop_inspection_template WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ShopInspectionTemplate>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateInspectionTemplate(Guid tenantId, string name)
        {
            const string sql = @"
                INSERT INTO shop_inspection_template (tenant_id, name) VALUES (@tenantId, @name)
                RETURNING id";
            return (await _db.Query<Guid>(sql, new { tenantId, name })).First();
        }

        public async Task<int> UpdateInspectionTemplate(Guid id, Guid tenantId, string name, bool isActive)
        {
            const string sql = @"
                UPDATE shop_inspection_template SET name = @name, is_active = @isActive
                WHERE id = @id AND tenant_id = @tenantId";
            return await _db.Execute(sql, new { id, tenantId, name, isActive });
        }

        /// <summary>
        /// Makes one template the default. Clears the previous one first: the unique index allows
        /// only one default per tenant, so setting without clearing would just fail.
        /// </summary>
        public async Task SetDefaultInspectionTemplate(Guid id, Guid tenantId)
        {
            await _db.Execute(
                "UPDATE shop_inspection_template SET is_default = false WHERE tenant_id = @tenantId AND is_default",
                new { tenantId });
            await _db.Execute(
                "UPDATE shop_inspection_template SET is_default = true WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId });
        }

        /// <summary>Adds or edits a checklist row. Tenant scope comes via the parent template.</summary>
        public async Task<Guid> UpsertTemplateItem(ShopInspectionTemplateItem item, Guid tenantId)
        {
            if (item.Id == Guid.Empty)
            {
                const string ins = @"
                    INSERT INTO shop_inspection_template_item (template_id, group_label, label, sort_order, is_active)
                    SELECT @TemplateId, @GroupLabel, @Label, @SortOrder, @IsActive
                    WHERE EXISTS (SELECT 1 FROM shop_inspection_template t
                                  WHERE t.id = @TemplateId AND t.tenant_id = @tenantId)
                    RETURNING id";
                return (await _db.Query<Guid>(ins, new
                {
                    item.TemplateId, item.GroupLabel, item.Label, item.SortOrder, item.IsActive, tenantId,
                })).FirstOrDefault();
            }
            const string upd = @"
                UPDATE shop_inspection_template_item i
                SET group_label = @GroupLabel, label = @Label, sort_order = @SortOrder, is_active = @IsActive
                FROM shop_inspection_template t
                WHERE i.id = @Id AND i.template_id = t.id AND t.tenant_id = @tenantId";
            await _db.Execute(upd, new
            {
                item.Id, item.GroupLabel, item.Label, item.SortOrder, item.IsActive, tenantId,
            });
            return item.Id;
        }

        /// <summary>
        /// Removes a checklist row. Past inspections are unaffected: their results snapshot the
        /// label and only null out the template link.
        /// </summary>
        public async Task<int> DeleteTemplateItem(Guid itemId, Guid tenantId)
        {
            const string sql = @"
                DELETE FROM shop_inspection_template_item i
                USING shop_inspection_template t
                WHERE i.id = @itemId AND i.template_id = t.id AND t.tenant_id = @tenantId";
            return await _db.Execute(sql, new { itemId, tenantId });
        }

        public async Task<List<ShopInspectionTemplateItem>> ListTemplateItems(Guid templateId)
        {
            const string sql = @"
                SELECT id, template_id AS TemplateId, group_label AS GroupLabel, label,
                       sort_order AS SortOrder, is_active AS IsActive
                FROM shop_inspection_template_item
                WHERE template_id = @templateId AND is_active = true
                ORDER BY sort_order, label";
            return (await _db.Query<ShopInspectionTemplateItem>(sql, new { templateId })).ToList();
        }

        private const string InspectionCols = @"
            id, tenant_id AS TenantId, customer_bike_id AS CustomerBikeId,
            work_order_id AS WorkOrderId, template_id AS TemplateId,
            performed_by_user_id AS PerformedByUserId, status, performed_at AS PerformedAt,
            next_service_date AS NextServiceDate, summary_notes AS SummaryNotes,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        /// <summary>Starts an inspection, materialising one result row per active checklist item.</summary>
        public async Task<Guid> CreateInspection(ShopInspection insp, IEnumerable<ShopInspectionResult> results)
        {
            const string sql = @"
                INSERT INTO shop_inspection (tenant_id, customer_bike_id, work_order_id, template_id,
                    performed_by_user_id, status, next_service_date, summary_notes)
                VALUES (@TenantId, @CustomerBikeId, @WorkOrderId, @TemplateId,
                    @PerformedByUserId, @Status, @NextServiceDate, @SummaryNotes)
                RETURNING id";
            var id = (await _db.Query<Guid>(sql, insp)).First();

            const string line = @"
                INSERT INTO shop_inspection_result (inspection_id, template_item_id, group_label, label, rating, notes, sort_order)
                VALUES (@inspectionId, @TemplateItemId, @GroupLabel, @Label, @Rating, @Notes, @SortOrder)";
            foreach (var r in results)
                await _db.Execute(line, new { inspectionId = id, r.TemplateItemId, r.GroupLabel, r.Label, r.Rating, r.Notes, r.SortOrder });
            return id;
        }

        public async Task<ShopInspectionWithResults?> GetInspection(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {InspectionCols} FROM shop_inspection WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            var insp = (await _db.Query<ShopInspection>(sql, new { id, tenantId })).FirstOrDefault();
            if (insp is null) return null;

            const string rsql = @"
                SELECT id, inspection_id AS InspectionId, template_item_id AS TemplateItemId,
                       group_label AS GroupLabel, label, rating, notes, sort_order AS SortOrder
                FROM shop_inspection_result WHERE inspection_id = @id ORDER BY sort_order, label";
            var results = (await _db.Query<ShopInspectionResult>(rsql, new { id })).ToList();

            var withResults = new ShopInspectionWithResults
            {
                Id = insp.Id, TenantId = insp.TenantId, CustomerBikeId = insp.CustomerBikeId,
                WorkOrderId = insp.WorkOrderId, TemplateId = insp.TemplateId,
                PerformedByUserId = insp.PerformedByUserId, Status = insp.Status,
                PerformedAt = insp.PerformedAt, NextServiceDate = insp.NextServiceDate,
                SummaryNotes = insp.SummaryNotes, CreatedAt = insp.CreatedAt, UpdatedAt = insp.UpdatedAt,
                Results = results,
            };
            withResults.RecountFromResults();
            return withResults;
        }

        /// <summary>Every inspection on a bike, newest first. The per-machine grading history.</summary>
        public async Task<List<ShopInspection>> ListInspectionsForBike(Guid bikeId, Guid tenantId)
        {
            // Counts come from SQL rather than loading every result row: the history panel only
            // needs the headline numbers.
            var sql = $@"SELECT {InspectionCols.Replace("id,", "i.id,")},
                    (SELECT count(*) FROM shop_inspection_result r
                      WHERE r.inspection_id = i.id AND r.rating = 'attention')::int AS AttentionCount,
                    (SELECT count(*) FROM shop_inspection_result r
                      WHERE r.inspection_id = i.id AND r.rating = 'monitor')::int AS MonitorCount
                 FROM shop_inspection i
                 WHERE i.customer_bike_id = @bikeId AND i.tenant_id = @tenantId
                 ORDER BY i.performed_at DESC";
            return (await _db.Query<ShopInspection>(sql, new { bikeId, tenantId })).ToList();
        }

        public async Task<int> UpdateInspectionHeader(Guid id, Guid tenantId, string status,
            DateTime? nextServiceDate, string? summaryNotes)
        {
            const string sql = @"
                UPDATE shop_inspection
                SET status = @status, next_service_date = @nextServiceDate, summary_notes = @summaryNotes
                WHERE id = @id AND tenant_id = @tenantId";
            return await _db.Execute(sql, new { id, tenantId, status, nextServiceDate, summaryNotes });
        }

        /// <summary>Saves grades. Joined through the parent so a result cannot be written cross-tenant.</summary>
        public async Task SaveInspectionResults(Guid inspectionId, Guid tenantId,
            IEnumerable<(Guid Id, string Rating, string? Notes)> rows)
        {
            const string sql = @"
                UPDATE shop_inspection_result r
                SET rating = @rating, notes = @notes
                FROM shop_inspection i
                WHERE r.id = @id AND r.inspection_id = i.id
                  AND i.id = @inspectionId AND i.tenant_id = @tenantId";
            foreach (var row in rows)
                await _db.Execute(sql, new { id = row.Id, row.Rating, row.Notes, inspectionId, tenantId });
        }

        // ── Customer bikes ────────────────────────────────────────────────────────────────
        private const string BikeCols = @"
            id, tenant_id AS TenantId, customer_user_id AS CustomerUserId,
            customer_name AS CustomerName, customer_phone AS CustomerPhone,
            serial, brand, model, model_year AS ModelYear, color, size, notes,
            sold_item_id AS SoldItemId, created_at AS CreatedAt, updated_at AS UpdatedAt";

        public async Task<ShopCustomerBike?> GetCustomerBike(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {BikeCols} FROM shop_customer_bike WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ShopCustomerBike>(sql, new { id, tenantId })).FirstOrDefault();
        }

        /// <summary>
        /// The bike this serial already belongs to, if any. Case-insensitive to match the unique
        /// index, so a serial typed in a different case resolves to the same bike rather than
        /// minting a duplicate — this is what makes "has this been in before" work.
        /// </summary>
        public async Task<ShopCustomerBike?> FindCustomerBikeBySerial(string serial, Guid tenantId)
        {
            if (string.IsNullOrWhiteSpace(serial)) return null;
            var sql = $@"SELECT {BikeCols} FROM shop_customer_bike
                         WHERE tenant_id = @tenantId AND lower(serial) = lower(@serial) LIMIT 1";
            return (await _db.Query<ShopCustomerBike>(sql, new { tenantId, serial = serial.Trim() })).FirstOrDefault();
        }

        /// <summary>Bikes belonging to a customer, by account or (for walk-ins) by phone.</summary>
        public async Task<List<ShopCustomerBike>> ListCustomerBikes(Guid tenantId, Guid? customerUserId, string? phone)
        {
            if (customerUserId is null && string.IsNullOrWhiteSpace(phone)) return new List<ShopCustomerBike>();
            var sql = $@"SELECT {BikeCols} FROM shop_customer_bike
                         WHERE tenant_id = @tenantId
                           AND ((@customerUserId::uuid IS NOT NULL AND customer_user_id = @customerUserId)
                             OR (@phone::text IS NOT NULL AND customer_phone = @phone))
                         ORDER BY updated_at DESC";
            return (await _db.Query<ShopCustomerBike>(sql, new
            {
                tenantId, customerUserId,
                phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            })).ToList();
        }

        public async Task<Guid> CreateCustomerBike(ShopCustomerBike b)
        {
            const string sql = @"
                INSERT INTO shop_customer_bike
                    (tenant_id, customer_user_id, customer_name, customer_phone, serial,
                     brand, model, model_year, color, size, notes, sold_item_id)
                VALUES
                    (@TenantId, @CustomerUserId, @CustomerName, @CustomerPhone, @Serial,
                     @Brand, @Model, @ModelYear, @Color, @Size, @Notes, @SoldItemId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, b)).First();
        }

        public async Task<int> UpdateCustomerBike(ShopCustomerBike b)
        {
            const string sql = @"
                UPDATE shop_customer_bike SET
                    customer_user_id = @CustomerUserId, customer_name = @CustomerName,
                    customer_phone = @CustomerPhone, serial = @Serial, brand = @Brand,
                    model = @Model, model_year = @ModelYear, color = @Color, size = @Size,
                    notes = @Notes, sold_item_id = @SoldItemId
                WHERE id = @Id AND tenant_id = @TenantId";
            return await _db.Execute(sql, b);
        }

        /// <summary>Every job on this bike, newest first. The answer to "what have we done to it?".</summary>
        public async Task<List<ShopBikeHistoryRow>> ListBikeHistory(Guid bikeId, Guid tenantId, int limit = 50)
        {
            const string sql = @"
                SELECT w.id AS WorkOrderId, w.status, w.created_at AS CreatedAt,
                       w.promised_at AS PromisedAt, w.intake_notes AS IntakeNotes,
                       COALESCE((SELECT sum(l.unit_price_cents * l.quantity)::int
                                 FROM shop_work_order_line l WHERE l.work_order_id = w.id), 0) AS TotalCents
                FROM shop_work_order w
                WHERE w.customer_bike_id = @bikeId AND w.tenant_id = @tenantId
                ORDER BY w.created_at DESC
                LIMIT @limit";
            return (await _db.Query<ShopBikeHistoryRow>(sql, new { bikeId, tenantId, limit })).ToList();
        }

        /// <summary>
        /// Did WE sell a unit with this serial? Feeds intake auto-fill: brand/model off the product,
        /// and the buyer off the sale, so a bike we sold doesn't get retyped from scratch.
        /// </summary>
        public async Task<ShopSoldUnitMatch?> FindSoldUnitBySerial(string serial, Guid tenantId)
        {
            if (string.IsNullOrWhiteSpace(serial)) return null;
            const string sql = @"
                SELECT i.id AS ItemId, i.serial,
                       p.brand, p.name AS Model,
                       s.buyer_user_id AS BuyerUserId, s.buyer_name AS BuyerName,
                       s.created_at AS SoldAt
                FROM shop_item i
                JOIN shop_variant v ON v.id = i.variant_id
                JOIN shop_product p ON p.id = v.product_id
                LEFT JOIN shop_sale_line sl ON sl.item_id = i.id
                LEFT JOIN shop_sale s ON s.id = sl.sale_id AND s.tenant_id = @tenantId
                WHERE i.tenant_id = @tenantId AND lower(i.serial) = lower(@serial)
                LIMIT 1";
            return (await _db.Query<ShopSoldUnitMatch>(sql, new { tenantId, serial = serial.Trim() })).FirstOrDefault();
        }

        // ── Service notifications ─────────────────────────────────────────────────────────

        /// <summary>Claims the "your bike is ready" notice for this work order, exactly once.
        /// Returns true only for the call that actually claimed it, so a double status flip (or
        /// two staff saving at once) cannot email the customer twice.</summary>
        public async Task<bool> TryClaimReadyNotice(Guid workOrderId, Guid tenantId)
        {
            const string sql = @"
                UPDATE shop_work_order
                SET ready_notified_at = now()
                WHERE id = @workOrderId AND tenant_id = @tenantId
                  AND status = 'ready' AND ready_notified_at IS NULL";
            return (await _db.Execute(sql, new { workOrderId, tenantId })) > 0;
        }

        /// <summary>Schedules (or clears) the follow-up reminder at pickup. days = 0 clears it,
        /// which is how a tenant with reminders switched off behaves.</summary>
        public Task ScheduleServiceReminder(Guid workOrderId, Guid tenantId, int days) => _db.Execute(
            @"UPDATE shop_work_order
              SET service_reminder_at = CASE WHEN @days > 0 THEN now() + (@days || ' days')::interval END,
                  updated_at = now()
              WHERE id = @workOrderId AND tenant_id = @tenantId",
            new { workOrderId, tenantId, days });

        /// <summary>Reminders that have come due and haven't been sent. Only orders that were
        /// actually picked up and have an email to send to.</summary>
        public async Task<List<ShopWorkOrder>> ListDueServiceReminders(int take)
        {
            var sql = $@"
                SELECT {WoCols} FROM shop_work_order
                WHERE service_reminder_at IS NOT NULL
                  AND reminder_sent_at IS NULL
                  AND service_reminder_at <= now()
                  AND status = 'picked_up'
                  AND customer_email IS NOT NULL
                ORDER BY service_reminder_at
                LIMIT @take";
            return (await _db.Query<ShopWorkOrder>(sql, new { take })).ToList();
        }

        /// <summary>Claims one reminder for sending. Same once-only guard as the ready notice, so
        /// a slow send or an overlapping sweep can't double-mail months later.</summary>
        public async Task<bool> TryClaimServiceReminder(Guid workOrderId)
        {
            const string sql = @"
                UPDATE shop_work_order SET reminder_sent_at = now()
                WHERE id = @workOrderId AND reminder_sent_at IS NULL";
            return (await _db.Execute(sql, new { workOrderId })) > 0;
        }

        // ── Job templates (saved standard repair jobs) ────────────────────────────────────

        private const string JobTemplateCols = @"
            id, tenant_id AS TenantId, name, fits_note AS FitsNote, notes,
            is_active AS IsActive, sort_order AS SortOrder,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string JobTemplateLineCols = @"
            id, template_id AS TemplateId, line_kind AS LineKind, description,
            variant_id AS VariantId, quantity, unit_price_cents AS UnitPriceCents,
            estimated_minutes AS EstimatedMinutes, sort_order AS SortOrder, created_at AS CreatedAt";

        public async Task<List<ShopJobTemplateWithLines>> ListJobTemplates(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? "AND is_active" : "";
            var sql = $@"SELECT {JobTemplateCols} FROM shop_job_template
                         WHERE tenant_id = @tenantId {filter}
                         ORDER BY sort_order, name";
            var templates = (await _db.Query<ShopJobTemplate>(sql, new { tenantId })).ToList();
            if (templates.Count == 0) return new List<ShopJobTemplateWithLines>();

            var ids = templates.Select(t => t.Id).ToArray();
            var lines = (await _db.Query<ShopJobTemplateLine>(
                $"SELECT {JobTemplateLineCols} FROM shop_job_template_line " +
                "WHERE template_id = ANY(@ids) ORDER BY sort_order, created_at", new { ids }))
                .GroupBy(l => l.TemplateId).ToDictionary(g => g.Key, g => g.ToList());

            return templates.Select(t => new ShopJobTemplateWithLines
            {
                Id = t.Id, TenantId = t.TenantId, Name = t.Name, FitsNote = t.FitsNote,
                Notes = t.Notes, IsActive = t.IsActive, SortOrder = t.SortOrder,
                CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt,
                Lines = lines.GetValueOrDefault(t.Id) ?? new(),
            }).ToList();
        }

        /// <summary>Creates or replaces a template and its lines in one transaction. Lines are
        /// replaced wholesale rather than diffed: a template is small and edited rarely, and a
        /// straight replace can't leave a half-updated job behind.</summary>
        public async Task<Guid> SaveJobTemplate(ShopJobTemplate t, IEnumerable<ShopJobTemplateLine> lines)
        {
            var id = t.Id == Guid.Empty ? Guid.NewGuid() : t.Id;
            var stmts = new List<(string Sql, object? Param)>
            {
                (@"INSERT INTO shop_job_template (id, tenant_id, name, fits_note, notes, is_active, sort_order)
                   VALUES (@id, @TenantId, @Name, @FitsNote, @Notes, @IsActive, @SortOrder)
                   ON CONFLICT (id) DO UPDATE SET
                       name = EXCLUDED.name, fits_note = EXCLUDED.fits_note, notes = EXCLUDED.notes,
                       is_active = EXCLUDED.is_active, sort_order = EXCLUDED.sort_order,
                       updated_at = now()
                   WHERE shop_job_template.tenant_id = @TenantId",
                    new { id, t.TenantId, t.Name, t.FitsNote, t.Notes, t.IsActive, t.SortOrder }),
                (@"DELETE FROM shop_job_template_line l USING shop_job_template t
                   WHERE l.template_id = @id AND t.id = l.template_id AND t.tenant_id = @TenantId",
                    new { id, t.TenantId }),
            };
            var order = 0;
            foreach (var l in lines)
            {
                order += 10;
                // The variant guard is in the statement: a part line can only reference a variant
                // this tenant owns, so a crafted id inserts nothing rather than borrowing stock.
                stmts.Add((@"
                    INSERT INTO shop_job_template_line
                        (template_id, line_kind, description, variant_id, quantity, unit_price_cents, estimated_minutes, sort_order)
                    SELECT @id, @LineKind, @Description, @VariantId, @Quantity, @UnitPriceCents, @EstimatedMinutes, @sortOrder
                    WHERE @VariantId::uuid IS NULL
                       OR EXISTS (SELECT 1 FROM shop_variant v
                                  WHERE v.id = @VariantId AND v.tenant_id = @TenantId)",
                    new
                    {
                        id, l.LineKind, l.Description, l.VariantId, l.Quantity, l.UnitPriceCents,
                        l.EstimatedMinutes, sortOrder = order, t.TenantId,
                    }));
            }
            await _db.ExecuteBatch(stmts);
            return id;
        }

        public async Task<int> DeleteJobTemplate(Guid id, Guid tenantId) => await _db.Execute(
            "DELETE FROM shop_job_template WHERE id = @id AND tenant_id = @tenantId",
            new { id, tenantId });

        /// <summary>Copies a template's lines onto a work order. PART prices resolve to the
        /// variant's CURRENT sale price unless the template pinned one, so applying a job saved
        /// last season doesn't quote last season's prices. Inactive variants are skipped and
        /// reported so the counter knows the job came across incomplete rather than silently
        /// short. Returns (linesAdded, skippedPartNames).</summary>
        public async Task<(int Added, List<string> Skipped)> ApplyJobTemplate(
            Guid templateId, Guid workOrderId, Guid tenantId)
        {
            const string linesSql = @"
                SELECT l.line_kind AS LineKind, l.description, l.variant_id AS VariantId,
                       l.quantity, l.unit_price_cents AS UnitPriceCents, l.estimated_minutes AS EstimatedMinutes,
                       v.sale_price_cents AS VariantPriceCents, v.is_active AS VariantActive,
                       p.name AS ProductName
                FROM shop_job_template_line l
                JOIN shop_job_template t ON t.id = l.template_id AND t.tenant_id = @tenantId
                LEFT JOIN shop_variant v ON v.id = l.variant_id AND v.tenant_id = @tenantId
                LEFT JOIN shop_product p ON p.id = v.product_id
                WHERE l.template_id = @templateId
                ORDER BY l.sort_order, l.created_at";
            var rows = (await _db.Query<JobTemplateApplyRow>(
                linesSql, new { templateId, tenantId })).ToList();
            if (rows.Count == 0) return (0, new List<string>());

            var stmts = new List<(string Sql, object? Param)>();
            var skipped = new List<string>();
            foreach (var r in rows)
            {
                if (r.LineKind == "part")
                {
                    // A part whose variant was deactivated (or belongs to another tenant, so the
                    // LEFT JOIN found nothing) can't be quoted. Report it instead of guessing.
                    if (r.VariantId is null || r.VariantActive != true)
                    {
                        skipped.Add(r.ProductName ?? "a part");
                        continue;
                    }
                }
                var price = r.UnitPriceCents ?? r.VariantPriceCents ?? 0;
                stmts.Add((@"
                    INSERT INTO shop_work_order_line
                        (work_order_id, line_kind, description, variant_id, quantity, unit_price_cents, estimated_minutes)
                    SELECT @workOrderId, @LineKind, @description, @VariantId, @quantity, @price, @EstimatedMinutes
                    FROM shop_work_order w
                    WHERE w.id = @workOrderId AND w.tenant_id = @tenantId",
                    new
                    {
                        workOrderId, tenantId, r.LineKind, description = r.Description,
                        r.VariantId, r.Quantity, price, r.EstimatedMinutes,
                    }));
            }
            if (stmts.Count > 0) await _db.ExecuteBatch(stmts);
            return (stmts.Count, skipped);
        }

        // Shape for the apply query only: a template line joined to its variant's live price.
        private sealed class JobTemplateApplyRow
        {
            public string LineKind { get; set; } = null!;
            public string? Description { get; set; }
            public Guid? VariantId { get; set; }
            public int Quantity { get; set; }
            public int? UnitPriceCents { get; set; }
            public int? EstimatedMinutes { get; set; }
            public int? VariantPriceCents { get; set; }
            public bool? VariantActive { get; set; }
            public string? ProductName { get; set; }
        }

        // ── Agreements (rental agreement / repair authorization) ─────────────────────────

        private const string AgreementCols = @"
            id, tenant_id AS TenantId, kind, version, title, body, is_active AS IsActive,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        public async Task<ShopAgreement?> GetActiveAgreement(Guid tenantId, string kind)
        {
            var sql = $@"SELECT {AgreementCols} FROM shop_agreement
                         WHERE tenant_id = @tenantId AND kind = @kind AND is_active
                         LIMIT 1";
            return (await _db.Query<ShopAgreement>(sql, new { tenantId, kind })).FirstOrDefault();
        }

        /// <summary>Publishes a new version and retires the current one, in one transaction so
        /// the "one active per kind" unique index is never transiently violated.</summary>
        public async Task<Guid> PublishAgreement(Guid tenantId, string kind, string title, string body)
        {
            var id = Guid.NewGuid();
            await _db.ExecuteBatch(new List<(string, object?)>
            {
                ("UPDATE shop_agreement SET is_active = false, updated_at = now() " +
                 "WHERE tenant_id = @tenantId AND kind = @kind AND is_active",
                    new { tenantId, kind }),
                (@"INSERT INTO shop_agreement (id, tenant_id, kind, version, title, body, is_active)
                   SELECT @id, @tenantId, @kind,
                          COALESCE(MAX(a.version), 0) + 1, @title, @body, true
                   FROM shop_agreement a WHERE a.tenant_id = @tenantId AND a.kind = @kind",
                    new { id, tenantId, kind, title, body }),
            });
            return id;
        }

        /// <summary>Records a signature. Tenant-guarded through the owner row, so a crafted id
        /// from another tenant signs nothing. Returns null when the owner isn't this tenant's.</summary>
        public async Task<Guid?> AddAgreementSignature(ShopAgreementSignature sig)
        {
            var ownerTable = sig.WorkOrderId.HasValue ? "shop_work_order" : "shop_rental";
            var ownerId = sig.WorkOrderId ?? sig.RentalId;
            var sql = $@"
                INSERT INTO shop_agreement_signature
                    (tenant_id, agreement_id, work_order_id, rental_id, agreement_version,
                     signer_name, signer_email, signature_data_url, ip_address, witnessed_by_user_id)
                SELECT o.tenant_id, @AgreementId, @WorkOrderId, @RentalId, @AgreementVersion,
                       @SignerName, @SignerEmail, @SignatureDataUrl, @IpAddress, @WitnessedByUserId
                FROM {ownerTable} o
                WHERE o.id = @ownerId AND o.tenant_id = @TenantId
                RETURNING id";
            var rows = await _db.Query<Guid>(sql, new
            {
                sig.TenantId, sig.AgreementId, sig.WorkOrderId, sig.RentalId, sig.AgreementVersion,
                sig.SignerName, sig.SignerEmail, sig.SignatureDataUrl, sig.IpAddress,
                sig.WitnessedByUserId, ownerId,
            });
            return rows.Count() == 0 ? null : rows.First();
        }

        private const string AgreementSigCols = @"
            id, tenant_id AS TenantId, agreement_id AS AgreementId, work_order_id AS WorkOrderId,
            rental_id AS RentalId, agreement_version AS AgreementVersion, signer_name AS SignerName,
            signer_email AS SignerEmail, signature_data_url AS SignatureDataUrl,
            signed_at AS SignedAt, ip_address AS IpAddress, witnessed_by_user_id AS WitnessedByUserId";

        public async Task<List<ShopAgreementSignature>> ListAgreementSignatures(
            Guid? workOrderId, Guid? rentalId, Guid tenantId)
        {
            var sql = $@"
                SELECT {AgreementSigCols} FROM shop_agreement_signature
                WHERE tenant_id = @tenantId
                  AND (@workOrderId::uuid IS NULL OR work_order_id = @workOrderId)
                  AND (@rentalId::uuid IS NULL OR rental_id = @rentalId)
                  AND (@workOrderId::uuid IS NOT NULL OR @rentalId::uuid IS NOT NULL)
                ORDER BY signed_at DESC";
            return (await _db.Query<ShopAgreementSignature>(
                sql, new { workOrderId, rentalId, tenantId })).ToList();
        }

        /// <summary>True when this rental already carries a signature against the CURRENTLY active
        /// rental agreement. A superseded signature doesn't count: if the terms changed, the
        /// renter agreed to different terms.</summary>
        public async Task<bool> HasCurrentAgreementSignature(Guid rentalId, Guid tenantId, string kind)
        {
            const string sql = @"
                SELECT COUNT(*) FROM shop_agreement_signature s
                JOIN shop_agreement a ON a.id = s.agreement_id
                WHERE s.rental_id = @rentalId AND s.tenant_id = @tenantId
                  AND a.kind = @kind AND a.is_active";
            return (await _db.ExecuteScalar(sql, new { rentalId, tenantId, kind })) > 0;
        }

        // ── Condition photos (work orders + rentals) ──────────────────────────────────────

        private const string ConditionPhotoCols = @"
            id, tenant_id AS TenantId, work_order_id AS WorkOrderId, rental_id AS RentalId,
            stage, image_url AS ImageUrl, caption, uploaded_by_user_id AS UploadedByUserId,
            sort_order AS SortOrder, created_at AS CreatedAt";

        /// <summary>Adds a photo. The owner is verified in the same statement (INSERT..SELECT
        /// against a tenant-scoped parent), so a crafted work-order or rental id from another
        /// tenant inserts nothing rather than attaching to their record.</summary>
        public async Task<Guid?> AddConditionPhoto(ShopConditionPhoto photo)
        {
            var ownerTable = photo.WorkOrderId.HasValue ? "shop_work_order" : "shop_rental";
            var ownerId = photo.WorkOrderId ?? photo.RentalId;
            var sql = $@"
                INSERT INTO shop_condition_photo
                    (tenant_id, work_order_id, rental_id, stage, image_url, caption,
                     uploaded_by_user_id, sort_order)
                SELECT o.tenant_id, @WorkOrderId, @RentalId, @Stage, @ImageUrl, @Caption,
                       @UploadedByUserId, @SortOrder
                FROM {ownerTable} o
                WHERE o.id = @ownerId AND o.tenant_id = @TenantId
                RETURNING id";
            var rows = await _db.Query<Guid>(sql, new
            {
                photo.TenantId, photo.WorkOrderId, photo.RentalId, photo.Stage, photo.ImageUrl,
                photo.Caption, photo.UploadedByUserId, photo.SortOrder, ownerId,
            });
            return rows.Count() == 0 ? null : rows.First();
        }

        public async Task<List<ShopConditionPhoto>> ListConditionPhotosForWorkOrder(Guid workOrderId, Guid tenantId)
        {
            var sql = $@"SELECT {ConditionPhotoCols} FROM shop_condition_photo
                         WHERE work_order_id = @workOrderId AND tenant_id = @tenantId
                         ORDER BY stage, sort_order, created_at";
            return (await _db.Query<ShopConditionPhoto>(sql, new { workOrderId, tenantId })).ToList();
        }

        public async Task<List<ShopConditionPhoto>> ListConditionPhotosForRental(Guid rentalId, Guid tenantId)
        {
            var sql = $@"SELECT {ConditionPhotoCols} FROM shop_condition_photo
                         WHERE rental_id = @rentalId AND tenant_id = @tenantId
                         ORDER BY stage, sort_order, created_at";
            return (await _db.Query<ShopConditionPhoto>(sql, new { rentalId, tenantId })).ToList();
        }

        /// <summary>How many photos this owner already has at this stage, for the per-stage cap.</summary>
        public async Task<int> CountConditionPhotos(Guid? workOrderId, Guid? rentalId, string stage, Guid tenantId)
        {
            const string sql = @"
                SELECT COUNT(*) FROM shop_condition_photo
                WHERE tenant_id = @tenantId AND stage = @stage
                  AND (@workOrderId::uuid IS NULL OR work_order_id = @workOrderId)
                  AND (@rentalId::uuid IS NULL OR rental_id = @rentalId)
                  AND (@workOrderId::uuid IS NOT NULL OR @rentalId::uuid IS NOT NULL)";
            return await _db.ExecuteScalar(sql, new { workOrderId, rentalId, stage, tenantId });
        }

        /// <summary>Deletes a photo and returns its stored URL so the caller can remove the file
        /// from image storage too. Null when it doesn't exist for this tenant.</summary>
        public async Task<string?> DeleteConditionPhoto(Guid id, Guid tenantId)
        {
            const string sql = @"
                DELETE FROM shop_condition_photo
                WHERE id = @id AND tenant_id = @tenantId
                RETURNING image_url";
            var rows = await _db.Query<string>(sql, new { id, tenantId });
            return rows.FirstOrDefault();
        }

        // ── Lesson rentables (shop_lesson_rentable: bikes offered with a lesson) ──────────────

        private const string LessonRentableCols = @"
            r.variant_id AS VariantId, r.price_cents_override AS PriceCentsOverride,
            p.name AS ProductName, p.description AS Description, p.image_url AS ImageUrl,
            v.size AS Size, v.color AS Color, v.gender AS Gender,
            v.daily_rate_cents AS DailyRateCents, v.deposit_cents AS DepositCents,
            v.tracking_kind AS TrackingKind, (v.is_active AND p.is_active) AS IsActive";

        public async Task<List<LessonRentableInfo>> ListLessonRentables(Guid eventId, Guid tenantId)
        {
            var sql = $@"
                SELECT {LessonRentableCols}
                FROM shop_lesson_rentable r
                JOIN event e ON e.id = r.event_id AND e.tenant_id = @tenantId
                JOIN shop_variant v ON v.id = r.variant_id
                JOIN shop_product p ON p.id = v.product_id
                WHERE r.event_id = @eventId
                ORDER BY p.name, v.size, v.color";
            return (await _db.Query<LessonRentableInfo>(sql, new { eventId, tenantId })).ToList();
        }

        public async Task<LessonRentableInfo?> GetLessonRentable(Guid eventId, Guid variantId, Guid tenantId)
        {
            var sql = $@"
                SELECT {LessonRentableCols}
                FROM shop_lesson_rentable r
                JOIN event e ON e.id = r.event_id AND e.tenant_id = @tenantId
                JOIN shop_variant v ON v.id = r.variant_id
                JOIN shop_product p ON p.id = v.product_id
                WHERE r.event_id = @eventId AND r.variant_id = @variantId";
            return (await _db.Query<LessonRentableInfo>(sql, new { eventId, variantId, tenantId })).FirstOrDefault();
        }

        public async Task ReplaceLessonRentables(Guid eventId, Guid tenantId,
            IEnumerable<(Guid VariantId, int? PriceCentsOverride)> rows)
        {
            // Both statements carry their own tenant guard: the delete only touches a tenant-owned
            // event, and each insert only lands when the variant belongs to the same tenant (a
            // crafted request can't attach another tenant's bike).
            var stmts = new List<(string Sql, object? Param)>
            {
                (@"DELETE FROM shop_lesson_rentable r USING event e
                   WHERE r.event_id = @eventId AND e.id = r.event_id AND e.tenant_id = @tenantId",
                    new { eventId, tenantId }),
            };
            foreach (var row in rows)
            {
                stmts.Add((@"
                    INSERT INTO shop_lesson_rentable (event_id, variant_id, price_cents_override)
                    SELECT e.id, v.id, @priceCentsOverride
                    FROM event e, shop_variant v
                    WHERE e.id = @eventId AND e.tenant_id = @tenantId
                      AND v.id = @variantId AND v.tenant_id = @tenantId
                    ON CONFLICT (event_id, variant_id) DO UPDATE
                        SET price_cents_override = EXCLUDED.price_cents_override",
                    new { eventId, tenantId, variantId = row.VariantId, priceCentsOverride = row.PriceCentsOverride }));
            }
            await _db.ExecuteBatch(stmts);
        }

        public async Task<(Guid Id, Guid ReceiptToken)> CreateRental(ShopRental rental, IEnumerable<ShopRentalLine> lines)
        {
            // Same shape as CreateSale: ids generated here so header + lines land in one transaction.
            var rentalId = Guid.NewGuid();
            var receipt = Guid.NewGuid();
            var stmts = new List<(string Sql, object? Param)>
            {
                (@"INSERT INTO shop_rental (id, tenant_id, renter_user_id, renter_name, renter_email, renter_phone,
                        waiver_signature_id, starts_at, ends_at, status, amount_cents, tax_cents, total_cents,
                        service_charge_cents, riders_required, deposit_cents, payment_method, sold_by_user_id, receipt_token, event_id)
                   VALUES (@id, @TenantId, @RenterUserId, @RenterName, @RenterEmail, @RenterPhone,
                        @WaiverSignatureId, @StartsAt, @EndsAt, @Status, @AmountCents, @TaxCents, @TotalCents,
                        @ServiceChargeCents, @RidersRequired, @DepositCents, @PaymentMethod, @SoldByUserId, @receipt, @EventId)",
                    new
                    {
                        id = rentalId, rental.TenantId, rental.RenterUserId, rental.RenterName, rental.RenterEmail,
                        rental.RenterPhone, rental.WaiverSignatureId, rental.StartsAt, rental.EndsAt, rental.Status,
                        rental.AmountCents, rental.TaxCents, rental.TotalCents, rental.ServiceChargeCents, rental.RidersRequired, rental.DepositCents,
                        rental.PaymentMethod, rental.SoldByUserId, receipt, rental.EventId,
                    }),
            };
            foreach (var l in lines)
            {
                stmts.Add((@"
                    INSERT INTO shop_rental_line (rental_id, variant_id, item_id, quantity, name_snapshot,
                        variant_label, daily_rate_cents_frozen, deposit_cents_frozen, line_amount_cents)
                    VALUES (@rentalId, @VariantId, @ItemId, @Quantity, @NameSnapshot, @VariantLabel,
                        @DailyRateCentsFrozen, @DepositCentsFrozen, @LineAmountCents)",
                    new
                    {
                        rentalId, l.VariantId, l.ItemId, l.Quantity, l.NameSnapshot, l.VariantLabel,
                        l.DailyRateCentsFrozen, l.DepositCentsFrozen, l.LineAmountCents,
                    }));
            }
            await _db.ExecuteBatch(stmts);
            return (rentalId, receipt);
        }

        public async Task<ShopRentalWithLines?> GetRental(Guid id, Guid tenantId)
        {
            var rental = (await _db.Query<ShopRentalWithLines>(
                $"SELECT {RentalCols} FROM shop_rental WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId })).FirstOrDefault();
            if (rental is null) return null;
            rental.Lines = (await _db.Query<ShopRentalLine>(
                $"SELECT {RentalLineCols} FROM shop_rental_line WHERE rental_id = @id ORDER BY created_at",
                new { id })).ToList();
            return rental;
        }

        public async Task<List<ShopRentalWithLines>> ListRentals(Guid tenantId, bool activeOnly, int limit)
        {
            var sql = $@"SELECT {RentalCols} FROM shop_rental
                        WHERE tenant_id = @tenantId
                        {(activeOnly ? "AND status IN ('pending','paid','out')" : "")}
                        ORDER BY starts_at DESC LIMIT @limit";
            var rentals = (await _db.Query<ShopRentalWithLines>(sql, new { tenantId, limit })).ToList();
            if (rentals.Count == 0) return rentals;
            var ids = rentals.Select(r => r.Id).ToArray();
            var lines = (await _db.Query<ShopRentalLine>(
                $"SELECT {RentalLineCols} FROM shop_rental_line WHERE rental_id = ANY(@ids) ORDER BY created_at",
                new { ids })).GroupBy(l => l.RentalId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var r in rentals) r.Lines = lines.GetValueOrDefault(r.Id) ?? new();
            return rentals;
        }

        public async Task<List<ShopRentalWithLines>> ListRentalsForUser(Guid userId, Guid tenantId, int limit)
        {
            var sql = $@"SELECT {RentalCols} FROM shop_rental
                        WHERE tenant_id = @tenantId AND renter_user_id = @userId
                          AND status IN ('pending','paid','out','returned','damaged')
                        ORDER BY starts_at DESC LIMIT @limit";
            var rentals = (await _db.Query<ShopRentalWithLines>(sql, new { tenantId, userId, limit })).ToList();
            if (rentals.Count == 0) return rentals;
            var ids = rentals.Select(r => r.Id).ToArray();
            var lines = (await _db.Query<ShopRentalLine>(
                $"SELECT {RentalLineCols} FROM shop_rental_line WHERE rental_id = ANY(@ids) ORDER BY created_at",
                new { ids })).GroupBy(l => l.RentalId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var r in rentals) r.Lines = lines.GetValueOrDefault(r.Id) ?? new();
            return rentals;
        }

        public async Task<ShopRental?> GetRentalByFeePaymentIntentId(string paymentIntentId) =>
            (await _db.Query<ShopRental>(
                $"SELECT {RentalCols} FROM shop_rental WHERE stripe_payment_intent_id = @paymentIntentId LIMIT 1",
                new { paymentIntentId })).FirstOrDefault();

        public async Task<ShopRental?> GetRentalByDepositPaymentIntentId(string paymentIntentId) =>
            (await _db.Query<ShopRental>(
                $"SELECT {RentalCols} FROM shop_rental WHERE deposit_pi_id = @paymentIntentId LIMIT 1",
                new { paymentIntentId })).FirstOrDefault();

        public Task SetRentalPaymentIntent(Guid id, string paymentIntentId) => _db.Execute(
            "UPDATE shop_rental SET stripe_payment_intent_id = @paymentIntentId, updated_at = now() WHERE id = @id",
            new { id, paymentIntentId });

        public Task SetRentalDepositIntent(Guid id, string paymentIntentId) => _db.Execute(
            "UPDATE shop_rental SET deposit_pi_id = @paymentIntentId, updated_at = now() WHERE id = @id",
            new { id, paymentIntentId });

        public Task MarkRentalDirectCharge(Guid id, Guid tenantId, string connectedAccountId) => _db.Execute(@"
            UPDATE shop_rental SET stripe_connected_account_id = @connectedAccountId,
                payment_method = 'stripe_direct', updated_at = now()
            WHERE id = @id AND tenant_id = @tenantId", new { id, tenantId, connectedAccountId });

        public async Task<bool> TryMarkRentalPaid(Guid id, Guid tenantId)
        {
            var rows = await _db.Query<Guid>(
                "UPDATE shop_rental SET status = 'paid', updated_at = now() WHERE id = @id AND tenant_id = @tenantId AND status = 'pending' RETURNING id",
                new { id, tenantId });
            return rows.Any();
        }

        public Task SetRentalOrderNumber(Guid id, int orderNumber) => _db.Execute(
            "UPDATE shop_rental SET order_number = @orderNumber, updated_at = now() WHERE id = @id",
            new { id, orderNumber });

        public Task MarkRentalFailed(Guid id) => _db.Execute(
            "UPDATE shop_rental SET status = 'failed', updated_at = now() WHERE id = @id AND status = 'pending'",
            new { id });

        public Task<int> CancelRental(Guid id, Guid tenantId) => _db.Execute(
            // Only before checkout — once gear is out, the path is Return, not Cancel.
            "UPDATE shop_rental SET status = 'cancelled', updated_at = now() WHERE id = @id AND tenant_id = @tenantId AND status IN ('pending','paid')",
            new { id, tenantId });

        public async Task<bool> CheckOutRental(Guid id, Guid tenantId, Guid? byUserId)
        {
            var rental = await GetRental(id, tenantId);
            if (rental is null || rental.Status != "paid") return false;

            var stmts = new List<(string Sql, object? Param)>
            {
                // Status guard repeated in SQL so a concurrent double-checkout can't run the stock
                // moves twice: only the statement that flips paid -> out proceeds meaningfully, and
                // ExecuteBatch runs all-or-nothing.
                ("UPDATE shop_rental SET status = 'out', checked_out_at = now(), updated_at = now() WHERE id = @id AND tenant_id = @tenantId AND status = 'paid'",
                    new { id, tenantId }),
            };
            foreach (var l in rental.Lines)
            {
                if (l.ItemId is null)
                {
                    stmts.Add((@"
                        WITH upd AS (
                            UPDATE shop_variant SET stock_on_hand = stock_on_hand - @qty, updated_at = now()
                            WHERE id = @variantId AND tenant_id = @tenantId RETURNING id
                        )
                        INSERT INTO shop_stock_movement
                            (tenant_id, variant_id, delta, reason, reference_kind, reference_id, created_by_user_id)
                        SELECT @tenantId, id, -@qty, 'rental_out', 'shop_rental', @id, @byUserId FROM upd",
                        new { variantId = l.VariantId, tenantId, qty = l.Quantity, id, byUserId }));
                }
                else
                {
                    stmts.Add(("UPDATE shop_item SET status = 'rented_out', updated_at = now() WHERE id = @itemId AND tenant_id = @tenantId AND status = 'available'",
                        new { itemId = l.ItemId, tenantId }));
                    stmts.Add((@"
                        INSERT INTO shop_stock_movement
                            (tenant_id, variant_id, item_id, delta, reason, reference_kind, reference_id, created_by_user_id)
                        VALUES (@tenantId, @variantId, @itemId, -1, 'rental_out', 'shop_rental', @id, @byUserId)",
                        new { tenantId, variantId = l.VariantId, itemId = l.ItemId, id, byUserId }));
                }
            }
            await _db.ExecuteBatch(stmts);
            return true;
        }

        public async Task<bool> ReturnRental(Guid id, Guid tenantId, Guid? byUserId, bool damaged,
            int depositCapturedCents, string? conditionNotes)
        {
            var rental = await GetRental(id, tenantId);
            if (rental is null || rental.Status != "out") return false;

            var status = damaged ? "damaged" : "returned";
            var stmts = new List<(string Sql, object? Param)>
            {
                (@"UPDATE shop_rental SET status = @status, returned_at = now(),
                        deposit_captured_cents = @depositCapturedCents, condition_notes = @conditionNotes,
                        updated_at = now()
                   WHERE id = @id AND tenant_id = @tenantId AND status = 'out'",
                    new { id, tenantId, status, depositCapturedCents, conditionNotes }),
            };
            foreach (var l in rental.Lines)
            {
                if (l.ItemId is null)
                {
                    stmts.Add((@"
                        WITH upd AS (
                            UPDATE shop_variant SET stock_on_hand = stock_on_hand + @qty, updated_at = now(),
                                low_stock_notified_at = CASE WHEN low_stock_threshold IS NOT NULL
                                                             AND stock_on_hand + @qty > low_stock_threshold
                                                             THEN NULL ELSE low_stock_notified_at END
                            WHERE id = @variantId AND tenant_id = @tenantId RETURNING id
                        )
                        INSERT INTO shop_stock_movement
                            (tenant_id, variant_id, delta, reason, reference_kind, reference_id, created_by_user_id)
                        SELECT @tenantId, id, @qty, 'rental_return', 'shop_rental', @id, @byUserId FROM upd",
                        new { variantId = l.VariantId, tenantId, qty = l.Quantity, id, byUserId }));
                }
                else
                {
                    // Only rented_out flips back — a unit moved to maintenance/retired mid-rental
                    // (damage triage) keeps that status.
                    stmts.Add(("UPDATE shop_item SET status = 'available', updated_at = now() WHERE id = @itemId AND tenant_id = @tenantId AND status = 'rented_out'",
                        new { itemId = l.ItemId, tenantId }));
                    stmts.Add((@"
                        INSERT INTO shop_stock_movement
                            (tenant_id, variant_id, item_id, delta, reason, reference_kind, reference_id, created_by_user_id)
                        VALUES (@tenantId, @variantId, @itemId, 1, 'rental_return', 'shop_rental', @id, @byUserId)",
                        new { tenantId, variantId = l.VariantId, itemId = l.ItemId, id, byUserId }));
                }
            }
            await _db.ExecuteBatch(stmts);
            return true;
        }

        // ── Work orders ───────────────────────────────────────────────────────────
        private const string WoCols = @"
            id, tenant_id AS TenantId, customer_user_id AS CustomerUserId, customer_name AS CustomerName,
            customer_phone AS CustomerPhone, customer_email AS CustomerEmail,
            subject_item_id AS SubjectItemId, customer_bike_desc AS CustomerBikeDesc,
            customer_bike_id AS CustomerBikeId, status,
            assigned_tech_user_id AS AssignedTechUserId, group_id AS GroupId, intake_notes AS IntakeNotes,
            customer_notes AS CustomerNotes,
            checked_by_user_id AS CheckedByUserId, checked_at AS CheckedAt,
            actual_minutes AS ActualMinutes, timer_started_at AS TimerStartedAt,
            promised_at AS PromisedAt, sale_id AS SaleId,
            deposit_cents AS DepositCents, deposit_pi_id AS DepositPiId, deposit_paid_at AS DepositPaidAt,
            deposit_payment_method AS DepositPaymentMethod, deposit_stripe_account_id AS DepositStripeAccountId,
            deposit_request_token AS DepositRequestToken, deposit_request_sent_at AS DepositRequestSentAt,
            deposit_refunded_cents AS DepositRefundedCents, deposit_refunded_at AS DepositRefundedAt,
            ready_notified_at AS ReadyNotifiedAt, service_reminder_at AS ServiceReminderAt,
            reminder_sent_at AS ReminderSentAt,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string WoLineCols = @"
            id, work_order_id AS WorkOrderId, line_kind AS LineKind, description, variant_id AS VariantId,
            quantity, unit_price_cents AS UnitPriceCents, labor_hours AS LaborHours, labor_rate_cents AS LaborRateCents,
            estimated_minutes AS EstimatedMinutes,
            consumed, approval_status AS ApprovalStatus, approval_at AS ApprovalAt, approval_by_user_id AS ApprovalByUserId,
            po_line_id AS PoLineId,
            arrived_at AS ArrivedAt, created_at AS CreatedAt";

        public async Task<Guid> CreateWorkOrder(ShopWorkOrder wo)
        {
            const string sql = @"
                INSERT INTO shop_work_order (tenant_id, customer_user_id, customer_name, customer_phone,
                    customer_email, subject_item_id, customer_bike_desc, customer_bike_id, status, assigned_tech_user_id,
                    group_id, intake_notes, customer_notes, promised_at)
                VALUES (@TenantId, @CustomerUserId, @CustomerName, @CustomerPhone,
                    @CustomerEmail, @SubjectItemId, @CustomerBikeDesc, @CustomerBikeId, @Status, @AssignedTechUserId,
                    @GroupId, @IntakeNotes, @CustomerNotes, @PromisedAt)
                RETURNING id";
            return (await _db.Query<Guid>(sql, wo)).First();
        }

        public Task<int> UpdateWorkOrder(ShopWorkOrder wo) => _db.Execute(@"
            UPDATE shop_work_order SET customer_user_id = @CustomerUserId, customer_name = @CustomerName,
                customer_phone = @CustomerPhone, customer_email = @CustomerEmail,
                subject_item_id = @SubjectItemId, customer_bike_desc = @CustomerBikeDesc,
                customer_bike_id = @CustomerBikeId, status = @Status,
                assigned_tech_user_id = @AssignedTechUserId, intake_notes = @IntakeNotes,
                customer_notes = @CustomerNotes,
                promised_at = @PromisedAt, updated_at = now()
            WHERE id = @Id AND tenant_id = @TenantId", wo);

        public async Task<ShopWorkOrderWithLines?> GetWorkOrder(Guid id, Guid tenantId)
        {
            var wo = (await _db.Query<ShopWorkOrder>(
                $"SELECT {WoCols} FROM shop_work_order WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId })).FirstOrDefault();
            if (wo is null) return null;
            var lines = (await _db.Query<ShopWorkOrderLine>(
                $"SELECT {WoLineCols} FROM shop_work_order_line WHERE work_order_id = @id ORDER BY created_at",
                new { id })).ToList();
            var withLines = ToWoWithLines(wo, lines);
            withLines.Notes = await ListWorkOrderNotes(id, tenantId);
            if (wo.GroupId is Guid gid)
                withLines.GroupMembers = await ListGroupMembers(gid, tenantId, excludeId: id);
            return withLines;
        }

        // ── Customer visit grouping ─────────────────────────────────────────────────

        /// <summary>Other bikes in the same visit (optionally excluding one), with a bike label,
        /// status and current line total for the visit panel.</summary>
        public async Task<List<ShopWorkOrderGroupMember>> ListGroupMembers(Guid groupId, Guid tenantId, Guid? excludeId = null) =>
            (await _db.Query<ShopWorkOrderGroupMember>(@"
                SELECT w.id AS Id,
                       COALESCE(w.customer_bike_desc, '(bike)') AS BikeLabel,
                       w.status AS Status,
                       COALESCE((SELECT SUM(l.unit_price_cents * l.quantity)
                                 FROM shop_work_order_line l
                                 WHERE l.work_order_id = w.id AND l.approval_status <> 'declined'), 0)::int AS TotalCents
                FROM shop_work_order w
                WHERE w.group_id = @groupId AND w.tenant_id = @tenantId
                  AND (@excludeId IS NULL OR w.id <> @excludeId)
                ORDER BY w.created_at",
                new { groupId, tenantId, excludeId })).ToList();

        /// <summary>Return the work order's visit group, creating one (a fresh shared key) if it has
        /// none. Atomic and idempotent; scoped by tenant. Null if the order isn't this tenant's.</summary>
        public async Task<Guid?> EnsureWorkOrderGroup(Guid workOrderId, Guid tenantId) =>
            (await _db.Query<Guid?>(@"
                UPDATE shop_work_order
                SET group_id = COALESCE(group_id, gen_random_uuid()), updated_at = now()
                WHERE id = @workOrderId AND tenant_id = @tenantId
                RETURNING group_id",
                new { workOrderId, tenantId })).FirstOrDefault();

        /// <summary>True when the tenant has at least one work order in this group (so a caller can
        /// only attach a new bike to a visit it already owns).</summary>
        public async Task<bool> GroupExistsForTenant(Guid groupId, Guid tenantId) =>
            (await _db.Query<int>(
                "SELECT 1 FROM shop_work_order WHERE group_id = @groupId AND tenant_id = @tenantId LIMIT 1",
                new { groupId, tenantId })).Any();

        /// <summary>Record or clear the QC sign-off. A non-null checker stamps checked_at now; a
        /// null checker clears both. Tenant-scoped; returns rows affected (0 = not this tenant's).</summary>
        public Task<int> SetWorkOrderQcCheck(Guid workOrderId, Guid tenantId, Guid? checkedByUserId) => _db.Execute(@"
            UPDATE shop_work_order
            SET checked_by_user_id = @checkedByUserId,
                checked_at = CASE WHEN @checkedByUserId IS NULL THEN NULL ELSE now() END,
                updated_at = now()
            WHERE id = @workOrderId AND tenant_id = @tenantId",
            new { workOrderId, tenantId, checkedByUserId });

        // ── Labor time tracking ─────────────────────────────────────────────────────

        public async Task<List<ShopLaborTimeRow>> GetLaborTimeReport(Guid tenantId, DateTime fromUtc, DateTime toUtc) =>
            (await _db.Query<ShopLaborTimeRow>(@"
                SELECT w.id AS WorkOrderId, w.created_at AS CreatedAt, w.customer_name AS CustomerName,
                       w.customer_bike_desc AS BikeLabel, w.status AS Status,
                       NULLIF(trim(concat(u.first_name, ' ', u.last_name)), '') AS TechName,
                       w.actual_minutes AS ActualMinutes,
                       COALESCE((SELECT SUM(l.estimated_minutes) FROM shop_work_order_line l
                                 WHERE l.work_order_id = w.id), 0)::int AS EstimatedMinutes
                FROM shop_work_order w
                LEFT JOIN users u ON u.id = w.assigned_tech_user_id
                WHERE w.tenant_id = @tenantId AND w.created_at >= @fromUtc AND w.created_at < @toUtc
                  AND (w.actual_minutes > 0
                       OR EXISTS (SELECT 1 FROM shop_work_order_line l
                                  WHERE l.work_order_id = w.id AND l.estimated_minutes IS NOT NULL))
                ORDER BY w.created_at DESC",
                new { tenantId, fromUtc, toUtc })).ToList();

        /// <summary>Start the job timer if it isn't already running. Tenant-scoped; returns rows
        /// affected (0 = not this tenant's, or already running).</summary>
        public Task<int> StartWorkOrderTimer(Guid workOrderId, Guid tenantId) => _db.Execute(@"
            UPDATE shop_work_order SET timer_started_at = now(), updated_at = now()
            WHERE id = @workOrderId AND tenant_id = @tenantId AND timer_started_at IS NULL",
            new { workOrderId, tenantId });

        /// <summary>Stop the running timer, folding the elapsed minutes into actual_minutes.</summary>
        public Task<int> StopWorkOrderTimer(Guid workOrderId, Guid tenantId) => _db.Execute(@"
            UPDATE shop_work_order
            SET actual_minutes = actual_minutes
                    + GREATEST(0, ROUND(EXTRACT(EPOCH FROM (now() - timer_started_at)) / 60))::int,
                timer_started_at = NULL, updated_at = now()
            WHERE id = @workOrderId AND tenant_id = @tenantId AND timer_started_at IS NOT NULL",
            new { workOrderId, tenantId });

        /// <summary>Set the accumulated actual minutes directly (manual correction), and stop the
        /// timer so the value stays authoritative.</summary>
        public Task<int> SetWorkOrderActualMinutes(Guid workOrderId, Guid tenantId, int minutes) => _db.Execute(@"
            UPDATE shop_work_order SET actual_minutes = @minutes, timer_started_at = NULL, updated_at = now()
            WHERE id = @workOrderId AND tenant_id = @tenantId",
            new { workOrderId, tenantId, minutes = Math.Max(0, minutes) });

        // ── Per-line approve/decline ────────────────────────────────────────────────

        /// <summary>Set one line's approval (pending clears the decision + who/when). Scoped through
        /// the parent order; returns rows affected (0 = not this tenant's line).</summary>
        public Task<int> SetLineApproval(Guid lineId, Guid tenantId, string status, Guid? byUserId) => _db.Execute(@"
            UPDATE shop_work_order_line l
            SET approval_status = @status,
                approval_at = CASE WHEN @status = 'pending' THEN NULL ELSE now() END,
                approval_by_user_id = CASE WHEN @status = 'pending' THEN NULL ELSE @byUserId END
            FROM shop_work_order wo
            WHERE l.id = @lineId AND wo.id = l.work_order_id AND wo.tenant_id = @tenantId",
            new { lineId, tenantId, status, byUserId });

        /// <summary>Approve every still-pending line on an order in one go.</summary>
        public Task<int> ApproveAllPendingLines(Guid workOrderId, Guid tenantId, Guid? byUserId) => _db.Execute(@"
            UPDATE shop_work_order_line l
            SET approval_status = 'approved', approval_at = now(), approval_by_user_id = @byUserId
            FROM shop_work_order wo
            WHERE l.work_order_id = wo.id AND wo.id = @workOrderId AND wo.tenant_id = @tenantId
              AND l.approval_status = 'pending'",
            new { workOrderId, tenantId, byUserId });

        // ── Work order statuses (tenant-customizable) ───────────────────────────────
        private const string WoStatusCols = @"
            id, tenant_id AS TenantId, code, name, color, behavior, notify_customer AS NotifyCustomer,
            sort_order AS SortOrder, is_builtin AS IsBuiltin, is_active AS IsActive, is_default AS IsDefault";

        /// <summary>Seeds the seven built-in statuses for a tenant that has none (new tenants; the
        /// migration handles existing ones). Idempotent via the per-tenant unique code index.</summary>
        public Task EnsureDefaultWorkOrderStatuses(Guid tenantId) => _db.Execute(@"
            INSERT INTO shop_work_order_status
                (tenant_id, code, name, color, behavior, notify_customer, sort_order, is_builtin, is_default)
            SELECT @tenantId, s.code, s.name, s.color, s.behavior, s.notify_customer, s.sort_order, true, s.is_default
            FROM (VALUES
                ('estimate',       'Estimate',         'grey',      'estimate',  false, 10, false),
                ('intake',         'Intake',           'blue-grey', 'open',      false, 20, true),
                ('awaiting_parts', 'Awaiting parts',   'warning',   'open',      false, 30, false),
                ('in_progress',    'In progress',      'indigo',    'open',      false, 40, false),
                ('ready',          'Ready for pickup', 'success',   'ready',     true,  50, false),
                ('picked_up',      'Picked up',        'primary',   'done',      false, 60, false),
                ('cancelled',      'Cancelled',        'error',     'cancelled', false, 70, false)
            ) AS s(code, name, color, behavior, notify_customer, sort_order, is_default)
            ON CONFLICT (tenant_id, lower(code)) DO NOTHING", new { tenantId });

        public async Task<List<ShopWorkOrderStatus>> ListWorkOrderStatuses(Guid tenantId, bool activeOnly = false)
        {
            await EnsureDefaultWorkOrderStatuses(tenantId);
            return (await _db.Query<ShopWorkOrderStatus>($@"
                SELECT {WoStatusCols} FROM shop_work_order_status
                WHERE tenant_id = @tenantId {(activeOnly ? "AND is_active = true" : "")}
                ORDER BY sort_order, name", new { tenantId })).ToList();
        }

        public async Task<ShopWorkOrderStatus?> GetWorkOrderStatus(Guid id, Guid tenantId) =>
            (await _db.Query<ShopWorkOrderStatus>(
                $"SELECT {WoStatusCols} FROM shop_work_order_status WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId })).FirstOrDefault();

        // Bulk drag-drop reorder: set each stage's sort_order in one round trip. Scoped by
        // tenant_id so a leaked id can't reorder another tenant's stages.
        public async Task UpdateWorkOrderStatusSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            const string sql = @"
                UPDATE shop_work_order_status AS s
                SET sort_order = data.sort_order
                FROM (SELECT unnest(@ids::uuid[]) AS id,
                             unnest(@orders::int[]) AS sort_order) AS data
                WHERE s.id = data.id AND s.tenant_id = @tenantId";
            await _db.Execute(sql, new { tenantId, ids = ids.ToArray(), orders = sortOrders.ToArray() });
        }

        /// <summary>Creates a custom status (always 'open' behavior) and returns it.</summary>
        public async Task<ShopWorkOrderStatus?> CreateWorkOrderStatus(Guid tenantId, string code, string name,
            string color, bool notifyCustomer, int sortOrder)
        {
            var id = Guid.NewGuid();
            await _db.Execute(@"
                INSERT INTO shop_work_order_status
                    (id, tenant_id, code, name, color, behavior, notify_customer, sort_order, is_builtin, is_active, is_default)
                VALUES (@id, @tenantId, @code, @name, @color, 'open', @notifyCustomer, @sortOrder, false, true, false)",
                new { id, tenantId, code, name, color, notifyCustomer, sortOrder });
            return await GetWorkOrderStatus(id, tenantId);
        }

        /// <summary>Updates the presentation fields of a status (built-in or custom). Code and
        /// behavior are never changed here, so the behavioral backbone stays intact.</summary>
        public Task<int> UpdateWorkOrderStatusPresentation(Guid id, Guid tenantId, string name, string color,
            bool notifyCustomer, int sortOrder, bool isActive) => _db.Execute(@"
            UPDATE shop_work_order_status
            SET name = @name, color = @color, notify_customer = @notifyCustomer,
                sort_order = @sortOrder, is_active = @isActive, updated_at = now()
            WHERE id = @id AND tenant_id = @tenantId",
            new { id, tenantId, name, color, notifyCustomer, sortOrder, isActive });

        /// <summary>Makes one status the sole default, in a transaction so there is never zero or two.</summary>
        public async Task<int> SetDefaultWorkOrderStatus(Guid id, Guid tenantId)
        {
            // The target must be an active, non-terminal status the tenant owns.
            var ok = (await _db.Query<string>(
                "SELECT behavior FROM shop_work_order_status WHERE id = @id AND tenant_id = @tenantId AND is_active = true",
                new { id, tenantId })).FirstOrDefault();
            if (ok is null || ok is "done" or "cancelled") return 0;
            await _db.ExecuteBatch(new List<(string, object?)>
            {
                ("UPDATE shop_work_order_status SET is_default = false, updated_at = now() WHERE tenant_id = @tenantId AND is_default = true",
                    new { tenantId }),
                ("UPDATE shop_work_order_status SET is_default = true, updated_at = now() WHERE id = @id AND tenant_id = @tenantId",
                    new { id, tenantId }),
            });
            return 1;
        }

        /// <summary>Count of work orders currently sitting in a status code (guards delete/deactivate).</summary>
        public async Task<int> CountWorkOrdersInStatus(Guid tenantId, string code) => await _db.ExecuteScalar(
            "SELECT count(*)::int FROM shop_work_order WHERE tenant_id = @tenantId AND status = @code",
            new { tenantId, code });

        /// <summary>Deletes a custom status. Built-ins and in-use statuses are refused (deactivate instead).</summary>
        public async Task<int> DeleteWorkOrderStatus(Guid id, Guid tenantId)
        {
            var st = await GetWorkOrderStatus(id, tenantId);
            if (st is null || st.IsBuiltin || st.IsDefault) return 0;
            if (await CountWorkOrdersInStatus(tenantId, st.Code) > 0) return 0;
            return await _db.Execute(
                "DELETE FROM shop_work_order_status WHERE id = @id AND tenant_id = @tenantId AND is_builtin = false",
                new { id, tenantId });
        }

        // ── Internal notes thread ───────────────────────────────────────────────────
        private const string WoNoteCols = @"
            n.id, n.work_order_id AS WorkOrderId, n.body, n.created_by_user_id AS CreatedByUserId,
            trim(concat(u.first_name, ' ', u.last_name)) AS CreatedByName, n.created_at AS CreatedAt";

        public async Task<List<ShopWorkOrderNote>> ListWorkOrderNotes(Guid workOrderId, Guid tenantId)
        {
            // Scoped by the note's own tenant_id (carried on the row), newest first.
            return (await _db.Query<ShopWorkOrderNote>($@"
                SELECT {WoNoteCols}
                FROM shop_work_order_note n
                LEFT JOIN users u ON u.id = n.created_by_user_id
                WHERE n.work_order_id = @workOrderId AND n.tenant_id = @tenantId
                ORDER BY n.created_at DESC", new { workOrderId, tenantId })).ToList();
        }

        public async Task<ShopWorkOrderNote?> AddWorkOrderNote(Guid workOrderId, Guid tenantId, string body, Guid? byUserId)
        {
            // Verify the parent belongs to this tenant before attaching a note to it.
            var ok = (await _db.Query<int>(
                "SELECT 1 FROM shop_work_order WHERE id = @workOrderId AND tenant_id = @tenantId",
                new { workOrderId, tenantId })).Any();
            if (!ok) return null;

            var id = Guid.NewGuid();
            await _db.Execute(@"
                INSERT INTO shop_work_order_note (id, tenant_id, work_order_id, body, created_by_user_id)
                VALUES (@id, @tenantId, @workOrderId, @body, @byUserId)",
                new { id, tenantId, workOrderId, body, byUserId });

            return (await _db.Query<ShopWorkOrderNote>($@"
                SELECT {WoNoteCols}
                FROM shop_work_order_note n
                LEFT JOIN users u ON u.id = n.created_by_user_id
                WHERE n.id = @id", new { id })).FirstOrDefault();
        }

        public async Task<List<ShopWorkOrderWithLines>> ListWorkOrders(Guid tenantId, bool includeClosed, int limit)
        {
            var sql = $@"SELECT {WoCols} FROM shop_work_order
                        WHERE tenant_id = @tenantId
                        {(includeClosed ? "" : "AND status NOT IN ('picked_up','cancelled')")}
                        ORDER BY created_at DESC LIMIT @limit";
            var orders = (await _db.Query<ShopWorkOrder>(sql, new { tenantId, limit })).ToList();
            if (orders.Count == 0) return new List<ShopWorkOrderWithLines>();
            var ids = orders.Select(o => o.Id).ToArray();
            var lines = (await _db.Query<ShopWorkOrderLine>(
                $"SELECT {WoLineCols} FROM shop_work_order_line WHERE work_order_id = ANY(@ids) ORDER BY created_at",
                new { ids })).GroupBy(l => l.WorkOrderId).ToDictionary(g => g.Key, g => g.ToList());
            return orders.Select(o => ToWoWithLines(o, lines.GetValueOrDefault(o.Id) ?? new())).ToList();
        }

        private static ShopWorkOrderWithLines ToWoWithLines(ShopWorkOrder o, List<ShopWorkOrderLine> lines) => new()
        {
            Id = o.Id, TenantId = o.TenantId, CustomerUserId = o.CustomerUserId, CustomerName = o.CustomerName,
            CustomerPhone = o.CustomerPhone, CustomerEmail = o.CustomerEmail, SubjectItemId = o.SubjectItemId,
            CustomerBikeDesc = o.CustomerBikeDesc, CustomerBikeId = o.CustomerBikeId, Status = o.Status, AssignedTechUserId = o.AssignedTechUserId,
            GroupId = o.GroupId, IntakeNotes = o.IntakeNotes, CustomerNotes = o.CustomerNotes,
            CheckedByUserId = o.CheckedByUserId, CheckedAt = o.CheckedAt,
            ActualMinutes = o.ActualMinutes, TimerStartedAt = o.TimerStartedAt,
            PromisedAt = o.PromisedAt, SaleId = o.SaleId,
            DepositCents = o.DepositCents, DepositPiId = o.DepositPiId, DepositPaidAt = o.DepositPaidAt,
            DepositPaymentMethod = o.DepositPaymentMethod, DepositStripeAccountId = o.DepositStripeAccountId,
            DepositRequestToken = o.DepositRequestToken, DepositRequestSentAt = o.DepositRequestSentAt,
            DepositRefundedCents = o.DepositRefundedCents, DepositRefundedAt = o.DepositRefundedAt,
            CreatedAt = o.CreatedAt, UpdatedAt = o.UpdatedAt, Lines = lines,
        };

        public async Task<Guid?> AddWorkOrderLine(ShopWorkOrderLine line, Guid tenantId, Guid? byUserId)
        {
            // Scope through the parent order (lines carry no tenant_id) and read its status: parts
            // on a committed order consume stock now; estimate lines wait for commitment.
            var status = (await _db.Query<string>(
                "SELECT status FROM shop_work_order WHERE id = @woId AND tenant_id = @tenantId",
                new { woId = line.WorkOrderId, tenantId })).FirstOrDefault();
            if (status is null) return null;
            if (line.LineKind == "part")
            {
                var variantOk = (await _db.Query<int>(
                    "SELECT 1 FROM shop_variant WHERE id = @variantId AND tenant_id = @tenantId",
                    new { variantId = line.VariantId, tenantId })).Any();
                if (!variantOk) return null;
            }

            var lineId = Guid.NewGuid();
            var consume = line.LineKind == "part" && status != "estimate" && status != "cancelled";
            var stmts = new List<(string Sql, object? Param)>
            {
                (@"INSERT INTO shop_work_order_line
                        (id, work_order_id, line_kind, description, variant_id, quantity, unit_price_cents,
                         labor_hours, labor_rate_cents, estimated_minutes, consumed)
                   VALUES (@lineId, @WorkOrderId, @LineKind, @Description, @VariantId, @Quantity, @UnitPriceCents,
                         @LaborHours, @LaborRateCents, @EstimatedMinutes, @consume)",
                    new { lineId, line.WorkOrderId, line.LineKind, line.Description, line.VariantId, line.Quantity,
                          line.UnitPriceCents, line.LaborHours, line.LaborRateCents, line.EstimatedMinutes, consume }),
            };
            if (consume)
            {
                stmts.Add(PartMovement(line.VariantId!.Value, tenantId, -line.Quantity, line.WorkOrderId, byUserId));
            }
            await _db.ExecuteBatch(stmts);
            return lineId;
        }

        public async Task<int> RemoveWorkOrderLine(Guid lineId, Guid tenantId, Guid? byUserId)
        {
            // Columns qualified with l. because of the tenant-scoping join (lines carry no tenant_id).
            var line = (await _db.Query<ShopWorkOrderLine>(@"
                SELECT l.id, l.work_order_id AS WorkOrderId, l.line_kind AS LineKind, l.description,
                       l.variant_id AS VariantId, l.quantity, l.unit_price_cents AS UnitPriceCents,
                       l.consumed, l.created_at AS CreatedAt
                FROM shop_work_order_line l
                JOIN shop_work_order wo ON wo.id = l.work_order_id AND wo.tenant_id = @tenantId
                WHERE l.id = @lineId", new { lineId, tenantId })).FirstOrDefault();
            if (line is null) return 0;

            var stmts = new List<(string Sql, object? Param)>
            {
                ("DELETE FROM shop_work_order_line WHERE id = @lineId", new { lineId }),
            };
            if (line.Consumed && line.VariantId is not null)
            {
                stmts.Add(PartMovement(line.VariantId.Value, tenantId, line.Quantity, line.WorkOrderId, byUserId));
            }
            await _db.ExecuteBatch(stmts);
            return 1;
        }

        public async Task ConsumePartsForWorkOrder(Guid workOrderId, Guid tenantId, Guid? byUserId)
        {
            // On-order parts (linked to a PO, not yet arrived) stay unconsumed until the receipt
            // lands — consuming a part the shelf doesn't hold would drive stock negative. A declined
            // line is never consumed: the customer refused it.
            var lines = (await PartLinesByConsumed(workOrderId, tenantId, consumed: false))
                .Where(l => (l.PoLineId is null || l.ArrivedAt is not null) && l.ApprovalStatus != "declined").ToList();
            if (lines.Count == 0) return;
            var stmts = new List<(string Sql, object? Param)>();
            foreach (var l in lines)
            {
                stmts.Add(("UPDATE shop_work_order_line SET consumed = true WHERE id = @id", new { id = l.Id }));
                stmts.Add(PartMovement(l.VariantId!.Value, tenantId, -l.Quantity, workOrderId, byUserId));
            }
            await _db.ExecuteBatch(stmts);
        }

        public async Task ReverseConsumedParts(Guid workOrderId, Guid tenantId, Guid? byUserId)
        {
            var lines = await PartLinesByConsumed(workOrderId, tenantId, consumed: true);
            if (lines.Count == 0) return;
            var stmts = new List<(string Sql, object? Param)>();
            foreach (var l in lines)
            {
                stmts.Add(("UPDATE shop_work_order_line SET consumed = false WHERE id = @id", new { id = l.Id }));
                stmts.Add(PartMovement(l.VariantId!.Value, tenantId, l.Quantity, workOrderId, byUserId));
            }
            await _db.ExecuteBatch(stmts);
        }

        private async Task<List<ShopWorkOrderLine>> PartLinesByConsumed(Guid workOrderId, Guid tenantId, bool consumed) =>
            (await _db.Query<ShopWorkOrderLine>($@"
                SELECT l.id, l.work_order_id AS WorkOrderId, l.line_kind AS LineKind, l.variant_id AS VariantId,
                       l.quantity, l.unit_price_cents AS UnitPriceCents, l.consumed,
                       l.approval_status AS ApprovalStatus, l.po_line_id AS PoLineId, l.arrived_at AS ArrivedAt
                FROM shop_work_order_line l
                JOIN shop_work_order wo ON wo.id = l.work_order_id AND wo.tenant_id = @tenantId
                WHERE l.work_order_id = @workOrderId AND l.line_kind = 'part' AND l.consumed = @consumed",
                new { workOrderId, tenantId, consumed })).ToList();

        // A signed repair_consume movement + the matching cached-count update, as one statement.
        // Negative delta = parts onto the bench; positive = reversal back to the shelf.
        private static (string Sql, object? Param) PartMovement(Guid variantId, Guid tenantId, int delta, Guid workOrderId, Guid? byUserId) =>
            (@"WITH upd AS (
                    UPDATE shop_variant SET stock_on_hand = stock_on_hand + @delta, updated_at = now()
                    WHERE id = @variantId AND tenant_id = @tenantId AND tracking_kind = 'pool' RETURNING id
                )
                INSERT INTO shop_stock_movement
                    (tenant_id, variant_id, delta, reason, reference_kind, reference_id, created_by_user_id)
                SELECT @tenantId, id, @delta, 'repair_consume', 'shop_work_order', @workOrderId, @byUserId FROM upd",
                new { variantId, tenantId, delta, workOrderId, byUserId });

        public Task SetWorkOrderSale(Guid workOrderId, Guid tenantId, Guid saleId) => _db.Execute(
            "UPDATE shop_work_order SET sale_id = @saleId, updated_at = now() WHERE id = @workOrderId AND tenant_id = @tenantId",
            new { workOrderId, tenantId, saleId });

        public Task MarkWorkOrderPickedUpBySale(Guid saleId) => _db.Execute(@"
            UPDATE shop_work_order SET status = 'picked_up', updated_at = now()
            WHERE sale_id = @saleId AND status <> 'picked_up'", new { saleId });

        // ── Work order deposits ───────────────────────────────────────────────────

        public Task<int> SetWorkOrderDeposit(Guid workOrderId, Guid tenantId, int depositCents) => _db.Execute(@"
            UPDATE shop_work_order SET deposit_cents = @depositCents, updated_at = now()
            WHERE id = @workOrderId AND tenant_id = @tenantId
              AND deposit_paid_at IS NULL AND deposit_refunded_at IS NULL",
            new { workOrderId, tenantId, depositCents });

        public Task MarkWorkOrderDepositRequestSent(Guid workOrderId, Guid tenantId) => _db.Execute(@"
            UPDATE shop_work_order SET deposit_request_sent_at = now(), updated_at = now()
            WHERE id = @workOrderId AND tenant_id = @tenantId", new { workOrderId, tenantId });

        public async Task<ShopWorkOrderWithLines?> GetWorkOrderByDepositToken(Guid token, Guid tenantId)
        {
            var id = (await _db.Query<Guid?>(
                "SELECT id FROM shop_work_order WHERE deposit_request_token = @token AND tenant_id = @tenantId",
                new { token, tenantId })).FirstOrDefault();
            return id is null ? null : await GetWorkOrder(id.Value, tenantId);
        }

        public Task SetWorkOrderDepositIntent(Guid workOrderId, Guid tenantId, string piId, string? stripeAccountId) => _db.Execute(@"
            UPDATE shop_work_order SET deposit_pi_id = @piId, deposit_stripe_account_id = @stripeAccountId, updated_at = now()
            WHERE id = @workOrderId AND tenant_id = @tenantId", new { workOrderId, tenantId, piId, stripeAccountId });

        public Task ClearWorkOrderDepositIntent(Guid workOrderId, Guid tenantId) => _db.Execute(@"
            UPDATE shop_work_order SET deposit_pi_id = NULL, deposit_stripe_account_id = NULL, updated_at = now()
            WHERE id = @workOrderId AND tenant_id = @tenantId AND deposit_paid_at IS NULL",
            new { workOrderId, tenantId });

        // Idempotent flip: exactly one caller (webhook vs reconciler vs counter) wins the right to
        // book the ledger entry, mirroring TryMarkSalePaid.
        public async Task<bool> TryMarkWorkOrderDepositPaid(Guid workOrderId, Guid tenantId, string paymentMethod) =>
            await _db.Execute(@"
                UPDATE shop_work_order SET deposit_paid_at = now(), deposit_payment_method = @paymentMethod, updated_at = now()
                WHERE id = @workOrderId AND tenant_id = @tenantId AND deposit_paid_at IS NULL",
                new { workOrderId, tenantId, paymentMethod }) > 0;

        public async Task<ShopWorkOrder?> GetWorkOrderByDepositPaymentIntentId(string paymentIntentId) =>
            (await _db.Query<ShopWorkOrder>(
                $"SELECT {WoCols} FROM shop_work_order WHERE deposit_pi_id = @paymentIntentId LIMIT 1",
                new { paymentIntentId })).FirstOrDefault();

        // Consumes part of the deposit (a partial refund, or a conversion to store credit).
        // Compare-and-swap on the running count (the caller passes what it read), so two
        // concurrent submissions can't both record: the loser matches nothing and must reload.
        // The fully-returned stamp sets itself when the count reaches the deposit.
        public async Task<bool> TryAddWorkOrderDepositRefund(Guid workOrderId, Guid tenantId, int cents, int expectedRefundedBefore) =>
            await _db.Execute(@"
                UPDATE shop_work_order
                SET deposit_refunded_cents = deposit_refunded_cents + @cents,
                    deposit_refunded_at = CASE WHEN deposit_refunded_cents + @cents >= deposit_cents
                                               THEN now() ELSE deposit_refunded_at END,
                    updated_at = now()
                WHERE id = @workOrderId AND tenant_id = @tenantId
                  AND deposit_paid_at IS NOT NULL
                  AND deposit_refunded_cents = @expectedRefundedBefore
                  AND deposit_refunded_cents + @cents <= deposit_cents",
                new { workOrderId, tenantId, cents, expectedRefundedBefore }) > 0;

        // ── Special orders (work-order lines riding on supplier POs) ──────────────

        // Scope through the parent order (lines carry no tenant_id); only an un-arrived part
        // line can be (re)pointed at a PO line. The caller validates the PO line's tenant.
        // If the line was already consumed (added to a committed job before staff realized the
        // part wasn't on the shelf), hand the phantom stock back; the real consumption happens
        // when the PO receipt lands, so on-hand doesn't sit negative while the part is in transit.
        public async Task<bool> LinkWorkOrderLineToPoLine(Guid lineId, Guid tenantId, Guid poLineId)
        {
            var line = (await _db.Query<ShopWorkOrderLine>(@"
                SELECT l.id, l.work_order_id AS WorkOrderId, l.line_kind AS LineKind, l.variant_id AS VariantId,
                       l.quantity, l.unit_price_cents AS UnitPriceCents, l.consumed,
                       l.po_line_id AS PoLineId, l.arrived_at AS ArrivedAt
                FROM shop_work_order_line l
                JOIN shop_work_order wo ON wo.id = l.work_order_id AND wo.tenant_id = @tenantId
                WHERE l.id = @lineId AND l.line_kind = 'part' AND l.arrived_at IS NULL",
                new { lineId, tenantId })).FirstOrDefault();
            if (line is null) return false;

            var stmts = new List<(string Sql, object? Param)>
            {
                ("UPDATE shop_work_order_line SET po_line_id = @poLineId, consumed = false WHERE id = @lineId",
                    new { lineId, poLineId }),
            };
            if (line.Consumed)
                stmts.Add(PartMovement(line.VariantId!.Value, tenantId, line.Quantity, line.WorkOrderId, null));
            await _db.ExecuteBatch(stmts);
            return true;
        }

        private class WoArrivalRow
        {
            public Guid LineId { get; set; }
            public Guid VariantId { get; set; }
            public int Quantity { get; set; }
            public bool Consumed { get; set; }
            public string ApprovalStatus { get; set; } = "pending";
            public Guid WorkOrderId { get; set; }
            public string Status { get; set; } = "";
            public string CustomerName { get; set; } = "";
            public string? CustomerEmail { get; set; }
            public string? CustomerBikeDesc { get; set; }
            public Guid? CustomerBikeId { get; set; }
        }

        /// <summary>
        /// After a PO line receipt: stamp arrived_at on work-order lines riding that PO line
        /// (once enough units have been received to satisfy each line), consume the parts for
        /// committed jobs, and advance any awaiting_parts order whose linked parts are all in
        /// ('ready' when the job is parts-only, 'in_progress' when bench work remains). Returns
        /// one entry per touched work order so the caller can notify the customer.
        /// </summary>
        public async Task<List<ShopWoArrival>> ProcessArrivalsForPoLine(Guid poLineId, Guid tenantId, Guid? byUserId)
        {
            var rows = (await _db.Query<WoArrivalRow>(@"
                SELECT l.id AS LineId, l.variant_id AS VariantId, l.quantity AS Quantity, l.consumed AS Consumed,
                       l.approval_status AS ApprovalStatus,
                       wo.id AS WorkOrderId, wo.status AS Status, wo.customer_name AS CustomerName,
                       wo.customer_email AS CustomerEmail, wo.customer_bike_desc AS CustomerBikeDesc
                FROM shop_work_order_line l
                JOIN shop_work_order wo ON wo.id = l.work_order_id AND wo.tenant_id = @tenantId
                JOIN shop_po_line pl ON pl.id = l.po_line_id
                WHERE l.po_line_id = @poLineId AND l.arrived_at IS NULL
                  AND pl.quantity_received >= l.quantity",
                new { poLineId, tenantId })).ToList();
            if (rows.Count == 0) return new List<ShopWoArrival>();

            var stmts = new List<(string Sql, object? Param)>();
            foreach (var r in rows)
            {
                // A closed/quoted job takes no stock: estimates consume on commit (the arrived
                // stamp is enough), cancelled/picked_up never consume again, and a declined line
                // is never consumed however it arrives.
                var consumeNow = !r.Consumed && r.ApprovalStatus != "declined"
                    && r.Status is not ("estimate" or "cancelled" or "picked_up");
                stmts.Add((consumeNow
                        ? "UPDATE shop_work_order_line SET arrived_at = now(), consumed = true WHERE id = @id"
                        : "UPDATE shop_work_order_line SET arrived_at = now() WHERE id = @id",
                    new { id = r.LineId }));
                if (consumeNow)
                    stmts.Add(PartMovement(r.VariantId, tenantId, -r.Quantity, r.WorkOrderId, byUserId));
            }
            await _db.ExecuteBatch(stmts);

            var arrivals = new List<ShopWoArrival>();
            foreach (var g in rows.GroupBy(r => r.WorkOrderId))
            {
                var first = g.First();
                string? newStatus = null;
                if (first.Status == "awaiting_parts")
                {
                    // All linked parts in? Pure special orders (no labor) go straight to pickup.
                    var state = (await _db.Query<(bool AllArrived, bool HasLabor)>(@"
                        SELECT bool_and(l.po_line_id IS NULL OR l.arrived_at IS NOT NULL) AS AllArrived,
                               bool_or(l.line_kind = 'labor') AS HasLabor
                        FROM shop_work_order_line l WHERE l.work_order_id = @woId",
                        new { woId = g.Key })).First();
                    if (state.AllArrived)
                    {
                        newStatus = state.HasLabor ? "in_progress" : "ready";
                        await _db.Execute(@"
                            UPDATE shop_work_order SET status = @newStatus, updated_at = now()
                            WHERE id = @woId AND tenant_id = @tenantId AND status = 'awaiting_parts'",
                            new { woId = g.Key, tenantId, newStatus });
                    }
                }
                arrivals.Add(new ShopWoArrival
                {
                    WorkOrderId = g.Key,
                    CustomerName = first.CustomerName,
                    CustomerEmail = first.CustomerEmail,
                    CustomerBikeDesc = first.CustomerBikeDesc,
                    NewStatus = newStatus,
                });
            }
            return arrivals;
        }

        // ── CSV import + variant matrix ───────────────────────────────────────────

        /// <summary>
        /// One-transaction catalog import (validated upstream): creates missing categories and
        /// suppliers by name, then products, variants, and opening-stock 'adjustment' movements
        /// so imported counts reconcile against the movement ledger like everything else.
        /// </summary>
        public async Task<ShopImportResult> ImportCatalog(Guid tenantId, List<ShopImportProduct> products, Guid? byUserId)
        {
            var result = new ShopImportResult();
            var stmts = new List<(string Sql, object? Param)>();

            var existingCategories = (await ListCategories(tenantId, activeOnly: false))
                .ToDictionary(c => c.Name.Trim().ToLowerInvariant(), c => c.Id);
            var existingSuppliers = (await ListSuppliers(tenantId, activeOnly: false))
                .ToDictionary(s => s.Name.Trim().ToLowerInvariant(), s => s.Id);

            Guid? ResolveByName(string? name, Dictionary<string, Guid> existing, string table, List<string> created)
            {
                if (string.IsNullOrWhiteSpace(name)) return null;
                var key = name.Trim().ToLowerInvariant();
                if (existing.TryGetValue(key, out var id)) return id;
                id = Guid.NewGuid();
                existing[key] = id;
                created.Add(name.Trim());
                stmts.Add(($"INSERT INTO {table} (id, tenant_id, name) VALUES (@id, @tenantId, @name)",
                    new { id, tenantId, name = name.Trim() }));
                return id;
            }

            foreach (var p in products)
            {
                var categoryId = ResolveByName(p.CategoryName, existingCategories, "shop_category", result.NewCategories);
                var supplierId = ResolveByName(p.SupplierName, existingSuppliers, "shop_supplier", result.NewSuppliers);
                var productId = Guid.NewGuid();
                stmts.Add((@"
                    INSERT INTO shop_product (id, tenant_id, category_id, supplier_id, name, description, brand,
                        is_sellable, is_rentable)
                    VALUES (@productId, @tenantId, @categoryId, @supplierId, @name, @description, @brand,
                        @sellable, @rentable)",
                    new
                    {
                        productId, tenantId, categoryId, supplierId,
                        name = p.Name.Trim(),
                        description = string.IsNullOrWhiteSpace(p.Description) ? null : p.Description.Trim(),
                        brand = string.IsNullOrWhiteSpace(p.Brand) ? null : p.Brand.Trim(),
                        sellable = p.Variants.Any(v => v.SalePriceCents is not null) || p.Variants.All(v => v.DailyRateCents is null),
                        rentable = p.Variants.Any(v => v.DailyRateCents is not null),
                    }));
                result.Products++;

                foreach (var v in p.Variants)
                {
                    var variantId = Guid.NewGuid();
                    stmts.Add((@"
                        INSERT INTO shop_variant (id, tenant_id, product_id, sku, barcode, size, color, gender,
                            sale_price_cents, cost_cents, daily_rate_cents, deposit_cents, tracking_kind,
                            stock_on_hand, low_stock_threshold)
                        VALUES (@variantId, @tenantId, @productId, @Sku, @Barcode, @Size, @Color, @Gender,
                            @SalePriceCents, @CostCents, @DailyRateCents, @DepositCents, @TrackingKind,
                            @Stock, @LowStockThreshold)",
                        new
                        {
                            variantId, tenantId, productId, v.Sku, v.Barcode, v.Size, v.Color, v.Gender,
                            v.SalePriceCents, v.CostCents, v.DailyRateCents, v.DepositCents, v.TrackingKind,
                            v.Stock, v.LowStockThreshold,
                        }));
                    if (v.Stock > 0)
                    {
                        stmts.Add((@"
                            INSERT INTO shop_stock_movement (tenant_id, variant_id, delta, reason, note, created_by_user_id)
                            VALUES (@tenantId, @variantId, @delta, 'adjustment', 'CSV import', @byUserId)",
                            new { tenantId, variantId, delta = v.Stock, byUserId }));
                    }
                    result.Variants++;
                }
            }

            await _db.ExecuteBatch(stmts);
            return result;
        }

        /// <summary>
        /// Size x color matrix for an existing product: inserts each missing combination,
        /// silently skipping ones that already exist (attr-combo or SKU unique collisions).
        /// Returns (created, skipped).
        /// </summary>
        public async Task<(int Created, int Skipped)> GenerateVariants(Guid tenantId, Guid productId,
            IReadOnlyList<(string? Size, string? Color)> combos, string? skuPrefix,
            int? salePriceCents, int? costCents, int depositCents, int? lowStockThreshold)
        {
            int created = 0, skipped = 0;
            foreach (var (size, color) in combos)
            {
                var skuParts = new[] { skuPrefix, size, color }
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim().ToUpperInvariant());
                var sku = string.IsNullOrWhiteSpace(skuPrefix) ? null : string.Join("-", skuParts);
                try
                {
                    var n = await _db.Execute(@"
                        INSERT INTO shop_variant (tenant_id, product_id, sku, size, color,
                            sale_price_cents, cost_cents, deposit_cents, tracking_kind, stock_on_hand, low_stock_threshold)
                        SELECT @tenantId, p.id, @sku, @size, @color,
                            @salePriceCents, @costCents, @depositCents, 'pool', 0, @lowStockThreshold
                        FROM shop_product p WHERE p.id = @productId AND p.tenant_id = @tenantId",
                        new { tenantId, productId, sku, size, color, salePriceCents, costCents, depositCents, lowStockThreshold });
                    if (n > 0) created++; else skipped++;   // 0 rows = product not in this tenant
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
                {
                    skipped++;   // combo or SKU already exists; the matrix is additive
                }
            }
            return (created, skipped);
        }

        // ── Inventory reports ─────────────────────────────────────────────────────

        // Valuation of what's owned right now. Pool variants value the cached count at the
        // variant cost; serialized variants count units still owned (available / maintenance /
        // rented_out) and sum each unit's own acquired cost (falling back to the variant cost).
        public async Task<List<ShopValuationRow>> GetValuationReport(Guid tenantId) =>
            (await _db.Query<ShopValuationRow>(@"
                SELECT v.id AS VariantId, p.name AS ProductName,
                       NULLIF(TRIM(CONCAT_WS(' / ', v.size, v.color)), '') AS VariantLabel,
                       v.sku, c.name AS CategoryName, v.tracking_kind AS TrackingKind,
                       v.cost_cents AS CostCents, v.sale_price_cents AS SalePriceCents,
                       CASE WHEN v.tracking_kind = 'serialized'
                            THEN (SELECT count(*)::int FROM shop_item i
                                  WHERE i.variant_id = v.id AND i.status IN ('available','maintenance','rented_out'))
                            ELSE v.stock_on_hand END AS OnHand,
                       CASE WHEN v.tracking_kind = 'serialized'
                            THEN (SELECT COALESCE(SUM(COALESCE(i.acquired_cost_cents, v.cost_cents, 0)), 0)::bigint
                                  FROM shop_item i
                                  WHERE i.variant_id = v.id AND i.status IN ('available','maintenance','rented_out'))
                            ELSE (v.stock_on_hand::bigint * COALESCE(v.cost_cents, 0)) END AS CostValueCents,
                       CASE WHEN v.tracking_kind = 'serialized'
                            THEN ((SELECT count(*) FROM shop_item i
                                   WHERE i.variant_id = v.id AND i.status IN ('available','maintenance','rented_out'))::bigint
                                  * COALESCE(v.sale_price_cents, 0))
                            ELSE (v.stock_on_hand::bigint * COALESCE(v.sale_price_cents, 0)) END AS RetailValueCents
                FROM shop_variant v
                JOIN shop_product p ON p.id = v.product_id
                LEFT JOIN shop_category c ON c.id = p.category_id
                WHERE v.tenant_id = @tenantId AND v.is_active
                ORDER BY c.name NULLS LAST, p.sort_order, p.name, v.size, v.color, v.sku",
                new { tenantId })).ToList();

        // Sold goods over a window: revenue is the discounted pre-tax line value; COGS prefers
        // the per-line cost snapshot (Script0197) and falls back to the variant's current cost
        // for historic lines. Labor lines (no variant) report as pure margin. Paid sales only;
        // refunded sales are excluded entirely.
        public async Task<List<ShopSalesReportRow>> GetSalesReport(Guid tenantId, DateTime fromUtc, DateTime toUtc) =>
            (await _db.Query<ShopSalesReportRow>(@"
                SELECT COALESCE(p.name, l.name_snapshot) AS ProductName,
                       l.variant_label AS VariantLabel, v.sku,
                       SUM(l.quantity)::int AS Units,
                       SUM(l.unit_price_cents::bigint * l.quantity - l.discount_cents)::bigint AS RevenueCents,
                       SUM(COALESCE(l.unit_cost_cents_frozen, v.cost_cents, 0)::bigint * l.quantity)::bigint AS CogsCents
                FROM shop_sale_line l
                JOIN shop_sale s ON s.id = l.sale_id AND s.tenant_id = @tenantId
                LEFT JOIN shop_variant v ON v.id = l.variant_id
                LEFT JOIN shop_product p ON p.id = v.product_id
                WHERE s.status = 'paid' AND s.created_at >= @fromUtc AND s.created_at < @toUtc
                GROUP BY COALESCE(p.name, l.name_snapshot), l.variant_label, v.sku
                ORDER BY RevenueCents DESC",
                new { tenantId, fromUtc, toUtc })).ToList();

        // Pool variants sitting on the shelf with no paid sale in the window (or ever).
        public async Task<List<ShopDeadStockRow>> GetDeadStockReport(Guid tenantId, DateTime cutoffUtc) =>
            (await _db.Query<ShopDeadStockRow>(@"
                SELECT v.id AS VariantId, p.name AS ProductName,
                       NULLIF(TRIM(CONCAT_WS(' / ', v.size, v.color)), '') AS VariantLabel,
                       v.sku, v.stock_on_hand AS OnHand,
                       (v.stock_on_hand::bigint * COALESCE(v.cost_cents, 0)) AS CostValueCents,
                       (SELECT MAX(s.created_at) FROM shop_sale_line l
                        JOIN shop_sale s ON s.id = l.sale_id AND s.status IN ('paid', 'refunded')
                        WHERE l.variant_id = v.id) AS LastSoldAt
                FROM shop_variant v
                JOIN shop_product p ON p.id = v.product_id
                WHERE v.tenant_id = @tenantId AND v.is_active
                  AND v.tracking_kind = 'pool' AND v.stock_on_hand > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM shop_sale_line l
                      JOIN shop_sale s ON s.id = l.sale_id AND s.status IN ('paid', 'refunded')
                      WHERE l.variant_id = v.id AND s.created_at >= @cutoffUtc)
                ORDER BY LastSoldAt NULLS FIRST, CostValueCents DESC",
                new { tenantId, cutoffUtc })).ToList();

        // ── Customer history (the Lightspeed-style profile view) ──────────────────
        // Matches by account id, email (lowercased), or phone (digits only; work orders only,
        // the other rows don't carry one), so walk-ins are findable by whatever they left.
        public async Task<(List<ShopSale> Sales, List<ShopRental> Rentals, List<ShopWorkOrder> WorkOrders)>
            GetCustomerHistory(Guid tenantId, Guid? userId, string? email, string? phone, int limit)
        {
            var e = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
            var p = string.IsNullOrWhiteSpace(phone) ? null : new string(phone.Where(char.IsDigit).ToArray());
            if (p is { Length: < 7 }) p = null;
            if (userId is null && e is null && p is null)
                return (new List<ShopSale>(), new List<ShopRental>(), new List<ShopWorkOrder>());

            var sales = (await _db.Query<ShopSale>($@"
                SELECT {SaleCols} FROM shop_sale
                WHERE tenant_id = @tenantId
                  AND ((@userId IS NOT NULL AND buyer_user_id = @userId)
                    OR (@e IS NOT NULL AND lower(buyer_email) = @e))
                ORDER BY created_at DESC LIMIT @limit",
                new { tenantId, userId, e, limit })).ToList();

            var rentals = (await _db.Query<ShopRental>($@"
                SELECT {RentalCols} FROM shop_rental
                WHERE tenant_id = @tenantId
                  AND ((@userId IS NOT NULL AND renter_user_id = @userId)
                    OR (@e IS NOT NULL AND lower(renter_email) = @e))
                ORDER BY starts_at DESC LIMIT @limit",
                new { tenantId, userId, e, limit })).ToList();

            var workOrders = (await _db.Query<ShopWorkOrder>($@"
                SELECT {WoCols} FROM shop_work_order
                WHERE tenant_id = @tenantId
                  AND ((@userId IS NOT NULL AND customer_user_id = @userId)
                    OR (@e IS NOT NULL AND lower(customer_email) = @e)
                    OR (@p IS NOT NULL AND regexp_replace(COALESCE(customer_phone, ''), '\D', '', 'g') = @p))
                ORDER BY created_at DESC LIMIT @limit",
                new { tenantId, userId, e, p, limit })).ToList();

            return (sales, rentals, workOrders);
        }

        // ── Sales history ─────────────────────────────────────────────────────────
        // Only these are sortable, and the map itself is the whitelist: the column never comes from
        // the request string, so a caller cannot inject through the ORDER BY.
        private static readonly Dictionary<string, string> SaleSortColumns = new()
        {
            ["createdAt"] = "s.created_at",
            ["orderNumber"] = "s.order_number",
            ["total"] = "s.total_cents",
            ["buyer"] = "lower(s.buyer_name)",
            ["status"] = "s.status",
        };

        public async Task<ShopSalesPage> SearchSales(Guid tenantId, ShopSaleQuery q)
        {
            // One predicate, reused for the count, the totals and the page, so the three can never
            // disagree about what "matching" means.
            var where = new List<string> { "s.tenant_id = @tenantId" };

            // Order number is numeric, so only compare it when the text actually is a number.
            var searchIsNumber = int.TryParse(q.Search?.Trim(), out var searchNumber);

            if (!string.IsNullOrWhiteSpace(q.Search))
            {
                where.Add($@"(
                    s.buyer_name ILIKE @search OR s.buyer_email ILIKE @search
                    {(searchIsNumber ? "OR s.order_number = @searchNumber" : "")}
                    OR EXISTS (SELECT 1 FROM shop_sale_line sl
                               WHERE sl.sale_id = s.id AND sl.name_snapshot ILIKE @search)
                )");
            }

            if (q.From.HasValue) where.Add("s.created_at >= @from");
            // Exclusive upper bound one day on, so "to = today" covers everything sold today rather
            // than only a sale stamped exactly midnight.
            if (q.To.HasValue) where.Add("s.created_at < @toExclusive");

            if (q.Statuses is { Count: > 0 }) where.Add("s.status = ANY(@statuses)");
            if (q.PaymentMethods is { Count: > 0 }) where.Add("s.payment_method = ANY(@paymentMethods)");
            if (!string.IsNullOrWhiteSpace(q.Channel)) where.Add("s.order_channel = @channel");
            if (q.SoldByUserId.HasValue) where.Add("s.sold_by_user_id = @soldByUserId");
            if (q.WorkOrderOnly) where.Add("s.work_order_id IS NOT NULL");
            if (q.AwaitingPickupOnly) where.Add(AwaitingPickupExpr);

            var whereSql = string.Join(" AND ", where);
            var pageSize = Math.Clamp(q.PageSize, 1, 200);
            var offset = Math.Max(0, q.Page - 1) * pageSize;
            var sortCol = SaleSortColumns.GetValueOrDefault(q.SortBy ?? "", "s.created_at");
            var sortDir = q.SortDesc ? "DESC" : "ASC";

            var args = new
            {
                tenantId,
                search = string.IsNullOrWhiteSpace(q.Search) ? null : $"%{q.Search.Trim()}%",
                searchNumber,
                from = q.From,
                toExclusive = q.To?.Date.AddDays(1),
                statuses = q.Statuses?.ToArray(),
                paymentMethods = q.PaymentMethods?.ToArray(),
                channel = q.Channel,
                soldByUserId = q.SoldByUserId,
                limit = pageSize,
                offset,
            };

            // The pickup badge is a tenant-wide work queue, so it deliberately ignores the filters:
            // filtering it would hide the queue exactly when someone is working it.
            var page = new ShopSalesPage
            {
                AwaitingPickupCount = await _db.ExecuteScalar(
                    $"SELECT count(*)::int FROM shop_sale s WHERE s.tenant_id = @tenantId AND {AwaitingPickupExpr}",
                    new { tenantId }),
            };

            var totalsSql = $@"
                SELECT count(*)::int AS Total,
                       COALESCE(SUM(s.total_cents) FILTER (WHERE s.status = 'paid'), 0)::bigint AS PaidCents,
                       COALESCE(SUM(s.total_cents) FILTER (WHERE s.status = 'refunded'), 0)::bigint AS RefundedCents,
                       COALESCE(SUM(s.tax_cents) FILTER (WHERE s.status = 'paid'), 0)::bigint AS TaxCents,
                       count(*) FILTER (WHERE s.status = 'paid')::int AS PaidCount,
                       count(*) FILTER (WHERE s.status = 'refunded')::int AS RefundedCount
                FROM shop_sale s WHERE {whereSql}";
            var t = (await _db.Query<ShopSaleTotalsRow>(totalsSql, args)).First();

            page.Total = t.Total;
            page.Totals = new ShopSalesTotals
            {
                PaidCents = t.PaidCents, RefundedCents = t.RefundedCents, TaxCents = t.TaxCents,
                PaidCount = t.PaidCount, RefundedCount = t.RefundedCount,
            };
            if (t.Total == 0) return page;

            // Unqualified columns in SaleCols resolve against the single aliased table.
            // See GetSale: query the derived type so no sale field can be dropped in transit.
            var sales = (await _db.Query<ShopSaleWithLines>(
                $@"SELECT {SaleCols}
                   FROM shop_sale s WHERE {whereSql}
                   ORDER BY {sortCol} {sortDir} NULLS LAST, s.created_at DESC
                   LIMIT @limit OFFSET @offset", args)).ToList();
            if (sales.Count == 0) return page;

            var ids = sales.Select(s => s.Id).ToArray();
            var lines = (await _db.Query<ShopSaleLine>(
                $"SELECT {SaleLineCols} FROM shop_sale_line WHERE sale_id = ANY(@ids) ORDER BY created_at",
                new { ids })).GroupBy(l => l.SaleId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var s in sales) s.Lines = lines.GetValueOrDefault(s.Id) ?? new();
            page.Rows = sales;
            return page;
        }

        public async Task<List<ShopSaleWithLines>> ListSalesForBuyer(Guid tenantId, Guid userId, int limit)
        {
            // work_order_id IS NULL: a repair bill-out is a sale against the customer's account,
            // but its lines are parts and labor; it reads wrong under "Orders".
            // status: 'pending' is an abandoned or in-flight checkout and must never look like a
            // purchase; 'refunded' IS included so a rider can see the refund happened rather than
            // watching the order silently vanish.
            var sales = (await _db.Query<ShopSaleWithLines>($@"
                SELECT {SaleCols} FROM shop_sale
                WHERE tenant_id = @tenantId AND buyer_user_id = @userId
                  AND work_order_id IS NULL
                  AND status IN ('paid', 'refunded')
                ORDER BY created_at DESC LIMIT @limit", new { tenantId, userId, limit })).ToList();
            if (sales.Count == 0) return sales;

            var ids = sales.Select(s => s.Id).ToArray();
            var lines = (await _db.Query<ShopSaleLine>(
                $"SELECT {SaleLineCols} FROM shop_sale_line WHERE sale_id = ANY(@ids) ORDER BY created_at",
                new { ids })).GroupBy(l => l.SaleId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var s in sales) s.Lines = lines.GetValueOrDefault(s.Id) ?? new();
            return sales;
        }

        // ── Stock takes ───────────────────────────────────────────────────────────
        public async Task<Guid> CreateStockCount(Guid tenantId, Guid? byUserId, string? notes)
        {
            var countId = Guid.NewGuid();
            // Header + snapshot in one transaction so a count always covers a consistent moment.
            await _db.ExecuteBatch(new List<(string Sql, object? Param)>
            {
                (@"INSERT INTO shop_stock_count (id, tenant_id, notes, started_by_user_id)
                   VALUES (@countId, @tenantId, @notes, @byUserId)",
                    new { countId, tenantId, notes, byUserId }),
                (@"INSERT INTO shop_stock_count_line (count_id, variant_id, expected_qty)
                   SELECT @countId, v.id, v.stock_on_hand
                   FROM shop_variant v
                   WHERE v.tenant_id = @tenantId AND v.is_active = true AND v.tracking_kind = 'pool'",
                    new { countId, tenantId }),
            });
            return countId;
        }

        public async Task<List<ShopStockCount>> ListStockCounts(Guid tenantId, int limit) =>
            (await _db.Query<ShopStockCount>(@"
                SELECT id, tenant_id AS TenantId, status, notes, started_by_user_id AS StartedByUserId,
                       started_at AS StartedAt, completed_at AS CompletedAt
                FROM shop_stock_count WHERE tenant_id = @tenantId
                ORDER BY started_at DESC LIMIT @limit", new { tenantId, limit })).ToList();

        public async Task<ShopStockCountWithLines?> GetStockCount(Guid id, Guid tenantId)
        {
            var count = (await _db.Query<ShopStockCount>(@"
                SELECT id, tenant_id AS TenantId, status, notes, started_by_user_id AS StartedByUserId,
                       started_at AS StartedAt, completed_at AS CompletedAt
                FROM shop_stock_count WHERE id = @id AND tenant_id = @tenantId", new { id, tenantId })).FirstOrDefault();
            if (count is null) return null;
            var lines = (await _db.Query<ShopStockCountLine>(@"
                SELECT l.id, l.count_id AS CountId, l.variant_id AS VariantId,
                       l.expected_qty AS ExpectedQty, l.counted_qty AS CountedQty,
                       p.name AS ProductName, v.sku AS Sku,
                       NULLIF(TRIM(BOTH ' / ' FROM COALESCE(v.size,'') ||
                           CASE WHEN v.color IS NOT NULL THEN ' / ' || v.color ELSE '' END), '') AS VariantLabel
                FROM shop_stock_count_line l
                JOIN shop_variant v ON v.id = l.variant_id
                JOIN shop_product p ON p.id = v.product_id
                WHERE l.count_id = @id
                ORDER BY p.name, v.size", new { id })).ToList();
            return new ShopStockCountWithLines
            {
                Id = count.Id, TenantId = count.TenantId, Status = count.Status, Notes = count.Notes,
                StartedByUserId = count.StartedByUserId, StartedAt = count.StartedAt,
                CompletedAt = count.CompletedAt, Lines = lines,
            };
        }

        public Task<int> SetStockCountLine(Guid lineId, Guid tenantId, int? countedQty) => _db.Execute(@"
            UPDATE shop_stock_count_line l SET counted_qty = @countedQty
            FROM shop_stock_count c
            WHERE l.id = @lineId AND c.id = l.count_id AND c.tenant_id = @tenantId AND c.status = 'open'",
            new { lineId, tenantId, countedQty });

        public async Task<bool> CompleteStockCount(Guid id, Guid tenantId, Guid? byUserId)
        {
            var status = (await _db.Query<string>(
                "SELECT status FROM shop_stock_count WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId })).FirstOrDefault();
            if (status != "open") return false;

            // Variance against CURRENT stock (it moved while staff counted), applied atomically with
            // the close: delta rows lock the variants, the update trues them up (resetting the
            // low-stock episode when the count lands above threshold), the movements record why.
            await _db.ExecuteBatch(new List<(string Sql, object? Param)>
            {
                (@"WITH delta AS (
                        SELECT v.id AS variant_id, l.counted_qty, l.counted_qty - v.stock_on_hand AS diff
                        FROM shop_stock_count_line l
                        JOIN shop_stock_count c ON c.id = l.count_id AND c.tenant_id = @tenantId AND c.status = 'open'
                        JOIN shop_variant v ON v.id = l.variant_id AND v.tenant_id = @tenantId
                        WHERE l.count_id = @id AND l.counted_qty IS NOT NULL AND l.counted_qty <> v.stock_on_hand
                        FOR UPDATE OF v
                    ),
                    upd AS (
                        UPDATE shop_variant v SET stock_on_hand = d.counted_qty, updated_at = now(),
                            low_stock_notified_at = CASE WHEN v.low_stock_threshold IS NOT NULL
                                                         AND d.counted_qty > v.low_stock_threshold
                                                         THEN NULL ELSE v.low_stock_notified_at END
                        FROM delta d WHERE v.id = d.variant_id
                    )
                    INSERT INTO shop_stock_movement
                        (tenant_id, variant_id, delta, reason, reference_kind, reference_id, created_by_user_id)
                    SELECT @tenantId, variant_id, diff, 'stocktake', 'shop_stock_count', @id, @byUserId FROM delta",
                    new { id, tenantId, byUserId }),
                (@"UPDATE shop_stock_count SET status = 'completed', completed_at = now()
                   WHERE id = @id AND tenant_id = @tenantId AND status = 'open'",
                    new { id, tenantId }),
            });
            return true;
        }

        public Task<int> CancelStockCount(Guid id, Guid tenantId) => _db.Execute(
            "UPDATE shop_stock_count SET status = 'cancelled' WHERE id = @id AND tenant_id = @tenantId AND status = 'open'",
            new { id, tenantId });
    }
}
