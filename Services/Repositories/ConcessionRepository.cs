using Services.Helpers.Interfaces;
using Services.Repositories.Data.ConcessionData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class ConcessionRepository : IConcessionRepository
    {
        // Selected with `FROM concession_product p LEFT JOIN concession_category c ON c.id = p.category_id`
        // so each product carries its category name + sort order for grouping/display.
        private const string ProductCols = @"
            p.id, p.tenant_id AS TenantId, p.name, p.description, p.category,
            p.category_id AS CategoryId, c.name AS CategoryName,
            COALESCE(c.sort_order, 2147483647) AS CategorySortOrder,
            p.price_cents AS PriceCents, p.image_url AS ImageUrl, p.show_in_carousel AS ShowInCarousel,
            p.is_active AS IsActive, p.sort_order AS SortOrder, p.station_id AS StationId,
            p.inventory, p.sold_out_date AS SoldOutDate, p.combo_available AS ComboAvailable,
            p.tax_category_id AS TaxCategoryId,
            p.created_at AS CreatedAt, p.updated_at AS UpdatedAt";

        private const string CategoryCols = @"
            id, tenant_id AS TenantId, name, sort_order AS SortOrder, is_active AS IsActive, created_at AS CreatedAt";

        private const string TaxCategoryCols = @"
            id, tenant_id AS TenantId, name, rate_bps AS RateBps, is_default AS IsDefault,
            sort_order AS SortOrder, is_active AS IsActive, created_at AS CreatedAt";

        private const string DiscountPresetCols = @"
            id, tenant_id AS TenantId, name, kind, value, is_active AS IsActive,
            sort_order AS SortOrder, created_at AS CreatedAt";

        private const string CompReasonCols = @"
            id, tenant_id AS TenantId, name, default_kind AS DefaultKind, default_value AS DefaultValue,
            is_active AS IsActive, sort_order AS SortOrder, created_at AS CreatedAt";

        private const string MenuSettingsCols = @"
            tenant_id AS TenantId, logo_url AS LogoUrl, background_color AS BackgroundColor,
            text_color AS TextColor, accent_color AS AccentColor, show_carousel AS ShowCarousel,
            carousel_seconds AS CarouselSeconds, tips_enabled AS TipsEnabled,
            prep_warn_minutes AS PrepWarnMinutes, prep_late_minutes AS PrepLateMinutes,
            ordering_hours AS OrderingHoursJson, ordering_seasons AS OrderingSeasonsJson,
            require_event_day AS RequireEventDay, prices_include_tax AS PricesIncludeTax,
            season_pass_discount_enabled AS SeasonPassDiscountEnabled,
            season_pass_discount_kind AS SeasonPassDiscountKind,
            season_pass_discount_value AS SeasonPassDiscountValue,
            loampass_discount_enabled AS LoampassDiscountEnabled,
            loampass_discount_kind AS LoampassDiscountKind,
            loampass_discount_value AS LoampassDiscountValue,
            require_manager_for_manual_discount AS RequireManagerForManualDiscount,
            seeded_at AS SeededAt, updated_at AS UpdatedAt";

        private const string VariantCols = @"
            id, product_id AS ProductId, size, color, price_cents AS PriceCents,
            image_url AS ImageUrl, inventory, is_active AS IsActive,
            sort_order AS SortOrder, created_at AS CreatedAt";

        private const string SaleCols = @"
            id, tenant_id AS TenantId, status, fulfillment_status AS FulfillmentStatus,
            order_number AS OrderNumber, subtotal_cents AS SubtotalCents, tip_cents AS TipCents,
            tax_cents AS TaxCents, prices_include_tax AS PricesIncludeTax,
            discount_cents AS DiscountCents, discount_kind AS DiscountKind, discount_label AS DiscountLabel,
            comp_reason_id AS CompReasonId, comp_reason_label AS CompReasonLabel,
            authorized_by_user_id AS AuthorizedByUserId, authorized_by_name AS AuthorizedByName,
            total_cents AS TotalCents, payment_method AS PaymentMethod,
            stripe_payment_intent_id AS StripePaymentIntentId,
            stripe_connected_account_id AS StripeConnectedAccountId,
            sold_by_user_id AS SoldByUserId, order_channel AS OrderChannel,
            purchaser_user_id AS PurchaserUserId, purchaser_email AS PurchaserEmail,
            purchaser_name AS PurchaserName, is_rush AS IsRush, created_at AS CreatedAt, paid_at AS PaidAt";

        private const string SaleLineCols = @"
            id, sale_id AS SaleId, product_id AS ProductId, variant_id AS VariantId,
            station_id AS StationId, name_snapshot AS NameSnapshot, variant_label AS VariantLabel,
            unit_price_cents AS UnitPriceCents, quantity, line_total_cents AS LineTotalCents,
            discount_cents AS DiscountCents, discount_kind AS DiscountKind, discount_label AS DiscountLabel,
            tax_cents AS TaxCents, tax_rate_bps AS TaxRateBps,
            prep_status AS PrepStatus, notes, parent_line_id AS ParentLineId, is_combo AS IsCombo,
            combo_tier AS ComboTier";

        private const string StationCols = @"
            id, tenant_id AS TenantId, name, sort_order AS SortOrder,
            is_active AS IsActive, created_at AS CreatedAt";

        private const string ModGroupCols = @"
            id, tenant_id AS TenantId, name, min_select AS MinSelect, max_select AS MaxSelect,
            is_required AS IsRequired, sort_order AS SortOrder, is_active AS IsActive, created_at AS CreatedAt";

        private const string ModOptionCols = @"
            id, group_id AS GroupId, name, price_delta_cents AS PriceDeltaCents,
            sort_order AS SortOrder, is_active AS IsActive";

        private readonly IDbHelper _db;

        public ConcessionRepository(IDbHelper db) => _db = db;

        // ── Products ──────────────────────────────────────────────────────────────
        public async Task<List<ConcessionProduct>> ListProducts(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? "AND p.is_active = true" : "";
            var sql = $@"
                SELECT {ProductCols}
                FROM concession_product p
                LEFT JOIN concession_category c ON c.id = p.category_id
                WHERE p.tenant_id = @tenantId {filter}
                ORDER BY p.sort_order, LOWER(p.name)";
            return (await _db.Query<ConcessionProduct>(sql, new { tenantId })).ToList();
        }

        public async Task<ConcessionProduct?> GetProduct(Guid id, Guid tenantId)
        {
            var sql = $@"SELECT {ProductCols} FROM concession_product p
                        LEFT JOIN concession_category c ON c.id = p.category_id
                        WHERE p.id = @id AND p.tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ConcessionProduct>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateProduct(ConcessionProduct p)
        {
            const string sql = @"
                INSERT INTO concession_product
                    (tenant_id, name, description, category_id, price_cents, image_url, show_in_carousel,
                     is_active, sort_order, station_id, inventory, combo_available, tax_category_id)
                VALUES (@TenantId, @Name, @Description, @CategoryId, @PriceCents, @ImageUrl, @ShowInCarousel,
                     @IsActive, @SortOrder, @StationId, @Inventory, @ComboAvailable, @TaxCategoryId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        public async Task UpdateProduct(ConcessionProduct p)
        {
            const string sql = @"
                UPDATE concession_product SET
                    name = @Name, description = @Description, category_id = @CategoryId,
                    price_cents = @PriceCents, image_url = @ImageUrl, inventory = @Inventory,
                    show_in_carousel = @ShowInCarousel, combo_available = @ComboAvailable,
                    tax_category_id = @TaxCategoryId,
                    is_active = @IsActive, sort_order = @SortOrder, station_id = @StationId, updated_at = now()
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

        // ── Categories ─────────────────────────────────────────────────────────────
        public async Task<List<ConcessionCategory>> ListCategories(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? "AND is_active = true" : "";
            var sql = $@"SELECT {CategoryCols} FROM concession_category
                        WHERE tenant_id = @tenantId {filter} ORDER BY sort_order, LOWER(name)";
            return (await _db.Query<ConcessionCategory>(sql, new { tenantId })).ToList();
        }

        public async Task<Guid> CreateCategory(ConcessionCategory cat)
        {
            const string sql = @"
                INSERT INTO concession_category (tenant_id, name, sort_order, is_active)
                VALUES (@TenantId, @Name, @SortOrder, @IsActive) RETURNING id";
            return (await _db.Query<Guid>(sql, cat)).First();
        }

        public async Task UpdateCategory(ConcessionCategory cat)
        {
            const string sql = @"UPDATE concession_category SET
                    name = @Name, sort_order = @SortOrder, is_active = @IsActive
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, cat);
        }

        public async Task DeleteCategory(Guid id, Guid tenantId)
        {
            // Products' category_id is ON DELETE SET NULL, so they fall back to "Uncategorized".
            await _db.Execute("DELETE FROM concession_category WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId });
        }

        // ── Tax categories ───────────────────────────────────────────────────────────
        // Lists the tenant's tax categories, ensuring a default exists first (covers tenants that
        // enabled F&B after the tax migration ran). Default sorts first.
        public async Task<List<ConcessionTaxCategory>> ListTaxCategories(Guid tenantId)
        {
            await EnsureDefaultTaxCategory(tenantId);
            var sql = $@"SELECT {TaxCategoryCols} FROM concession_tax_category
                        WHERE tenant_id = @tenantId
                        ORDER BY is_default DESC, sort_order, LOWER(name)";
            return (await _db.Query<ConcessionTaxCategory>(sql, new { tenantId })).ToList();
        }

        // Idempotently guarantees the tenant has a default (0%) tax category to anchor item rates. The
        // WHERE NOT EXISTS guard plus a swallowed unique-violation make it safe under concurrent first
        // sales (the partial unique index allows only one default per tenant).
        public async Task EnsureDefaultTaxCategory(Guid tenantId)
        {
            const string sql = @"
                INSERT INTO concession_tax_category (tenant_id, name, rate_bps, is_default, sort_order)
                SELECT @tenantId, 'Sales tax', 0, true, 0
                WHERE NOT EXISTS (SELECT 1 FROM concession_tax_category WHERE tenant_id = @tenantId)";
            try { await _db.Execute(sql, new { tenantId }); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* another request created it first */ }
        }

        public async Task<Guid> CreateTaxCategory(ConcessionTaxCategory c)
        {
            // A new default demotes any existing default (the partial unique index allows only one).
            if (c.IsDefault)
                await _db.Execute("UPDATE concession_tax_category SET is_default = false WHERE tenant_id = @TenantId",
                    new { c.TenantId });
            const string sql = @"
                INSERT INTO concession_tax_category (tenant_id, name, rate_bps, is_default, sort_order, is_active)
                VALUES (@TenantId, @Name, @RateBps, @IsDefault, @SortOrder, @IsActive) RETURNING id";
            return (await _db.Query<Guid>(sql, c)).First();
        }

        public async Task UpdateTaxCategory(ConcessionTaxCategory c)
        {
            if (c.IsDefault)
                await _db.Execute(
                    "UPDATE concession_tax_category SET is_default = false WHERE tenant_id = @TenantId AND id <> @Id",
                    new { c.TenantId, c.Id });
            const string sql = @"UPDATE concession_tax_category SET
                    name = @Name, rate_bps = @RateBps, is_default = @IsDefault,
                    sort_order = @SortOrder, is_active = @IsActive
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, c);
        }

        public async Task DeleteTaxCategory(Guid id, Guid tenantId)
        {
            // Products' tax_category_id is ON DELETE SET NULL, so they fall back to the tenant default.
            await _db.Execute(
                "DELETE FROM concession_tax_category WHERE id = @id AND tenant_id = @tenantId AND is_default = false",
                new { id, tenantId });
        }

        // ── Discount presets ─────────────────────────────────────────────────────────
        public async Task<List<ConcessionDiscountPreset>> ListDiscountPresets(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? "AND is_active = true" : "";
            var sql = $@"SELECT {DiscountPresetCols} FROM concession_discount_preset
                        WHERE tenant_id = @tenantId {filter} ORDER BY sort_order, LOWER(name)";
            return (await _db.Query<ConcessionDiscountPreset>(sql, new { tenantId })).ToList();
        }

        public async Task<ConcessionDiscountPreset?> GetDiscountPreset(Guid id, Guid tenantId)
        {
            var sql = $@"SELECT {DiscountPresetCols} FROM concession_discount_preset
                        WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ConcessionDiscountPreset>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateDiscountPreset(ConcessionDiscountPreset p)
        {
            const string sql = @"
                INSERT INTO concession_discount_preset (tenant_id, name, kind, value, is_active, sort_order)
                VALUES (@TenantId, @Name, @Kind, @Value, @IsActive, @SortOrder) RETURNING id";
            return (await _db.Query<Guid>(sql, p)).First();
        }

        public async Task UpdateDiscountPreset(ConcessionDiscountPreset p)
        {
            const string sql = @"UPDATE concession_discount_preset SET
                    name = @Name, kind = @Kind, value = @Value, is_active = @IsActive, sort_order = @SortOrder
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, p);
        }

        public async Task DeleteDiscountPreset(Guid id, Guid tenantId)
        {
            await _db.Execute("DELETE FROM concession_discount_preset WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId });
        }

        // ── Comp reasons ─────────────────────────────────────────────────────────────
        public async Task<List<ConcessionCompReason>> ListCompReasons(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? "AND is_active = true" : "";
            var sql = $@"SELECT {CompReasonCols} FROM concession_comp_reason
                        WHERE tenant_id = @tenantId {filter} ORDER BY sort_order, LOWER(name)";
            return (await _db.Query<ConcessionCompReason>(sql, new { tenantId })).ToList();
        }

        public async Task<ConcessionCompReason?> GetCompReason(Guid id, Guid tenantId)
        {
            var sql = $@"SELECT {CompReasonCols} FROM concession_comp_reason
                        WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ConcessionCompReason>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateCompReason(ConcessionCompReason c)
        {
            const string sql = @"
                INSERT INTO concession_comp_reason (tenant_id, name, default_kind, default_value, is_active, sort_order)
                VALUES (@TenantId, @Name, @DefaultKind, @DefaultValue, @IsActive, @SortOrder) RETURNING id";
            return (await _db.Query<Guid>(sql, c)).First();
        }

        public async Task UpdateCompReason(ConcessionCompReason c)
        {
            const string sql = @"UPDATE concession_comp_reason SET
                    name = @Name, default_kind = @DefaultKind, default_value = @DefaultValue,
                    is_active = @IsActive, sort_order = @SortOrder
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, c);
        }

        public async Task DeleteCompReason(Guid id, Guid tenantId)
        {
            await _db.Execute("DELETE FROM concession_comp_reason WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId });
        }

        // Comped sales in a window for the void/comp report (only sales that carry a comp reason), with
        // the cashier + authorizing-manager names joined for display. Newest first.
        public async Task<List<ConcessionSale>> SearchComps(Guid tenantId, DateTime fromUtc, DateTime toUtc, int take = 500)
        {
            var sql = $@"SELECT {SaleCols} FROM concession_sale
                        WHERE tenant_id = @tenantId AND comp_reason_id IS NOT NULL
                          AND status IN ('paid', 'refunded')
                          AND created_at >= @fromUtc AND created_at < @toUtc
                        ORDER BY created_at DESC LIMIT @take";
            return (await _db.Query<ConcessionSale>(sql, new { tenantId, fromUtc, toUtc, take })).ToList();
        }

        // ── Menu board settings ─────────────────────────────────────────────────────
        public async Task<ConcessionMenuSettings?> GetMenuSettings(Guid tenantId)
        {
            var sql = $"SELECT {MenuSettingsCols} FROM concession_menu_settings WHERE tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ConcessionMenuSettings>(sql, new { tenantId })).FirstOrDefault();
        }

        public async Task UpsertMenuSettings(ConcessionMenuSettings s)
        {
            const string sql = @"
                INSERT INTO concession_menu_settings
                    (tenant_id, logo_url, background_color, text_color, accent_color, show_carousel, carousel_seconds, tips_enabled, prep_warn_minutes, prep_late_minutes, ordering_hours, ordering_seasons, require_event_day, prices_include_tax,
                     season_pass_discount_enabled, season_pass_discount_kind, season_pass_discount_value,
                     loampass_discount_enabled, loampass_discount_kind, loampass_discount_value,
                     require_manager_for_manual_discount, updated_at)
                VALUES (@TenantId, @LogoUrl, @BackgroundColor, @TextColor, @AccentColor, @ShowCarousel, @CarouselSeconds, @TipsEnabled, @PrepWarnMinutes, @PrepLateMinutes, @OrderingHoursJson::jsonb, @OrderingSeasonsJson::jsonb, @RequireEventDay, @PricesIncludeTax,
                     @SeasonPassDiscountEnabled, @SeasonPassDiscountKind, @SeasonPassDiscountValue,
                     @LoampassDiscountEnabled, @LoampassDiscountKind, @LoampassDiscountValue,
                     @RequireManagerForManualDiscount, now())
                ON CONFLICT (tenant_id) DO UPDATE SET
                    logo_url = @LogoUrl, background_color = @BackgroundColor, text_color = @TextColor,
                    accent_color = @AccentColor, show_carousel = @ShowCarousel, carousel_seconds = @CarouselSeconds,
                    tips_enabled = @TipsEnabled, prep_warn_minutes = @PrepWarnMinutes, prep_late_minutes = @PrepLateMinutes,
                    ordering_hours = @OrderingHoursJson::jsonb, ordering_seasons = @OrderingSeasonsJson::jsonb,
                    require_event_day = @RequireEventDay, prices_include_tax = @PricesIncludeTax,
                    season_pass_discount_enabled = @SeasonPassDiscountEnabled, season_pass_discount_kind = @SeasonPassDiscountKind,
                    season_pass_discount_value = @SeasonPassDiscountValue,
                    loampass_discount_enabled = @LoampassDiscountEnabled, loampass_discount_kind = @LoampassDiscountKind,
                    loampass_discount_value = @LoampassDiscountValue,
                    require_manager_for_manual_discount = @RequireManagerForManualDiscount, updated_at = now()";
            await _db.Execute(sql, s);
        }

        // Stamp the tenant as having loaded the starter catalog (first seed wins; kept on re-seed). Upserts
        // so it works whether or not the tenant has a menu-settings row yet.
        public async Task MarkStarterSeeded(Guid tenantId)
        {
            const string sql = @"
                INSERT INTO concession_menu_settings (tenant_id, seeded_at, updated_at)
                VALUES (@tenantId, now(), now())
                ON CONFLICT (tenant_id) DO UPDATE SET seeded_at = COALESCE(concession_menu_settings.seeded_at, now())";
            await _db.Execute(sql, new { tenantId });
        }

        // ── Online-order capacity / throttle ─────────────────────────────────────────
        public async Task<ConcessionOrderingCapacity?> GetOrderingCapacity(Guid tenantId)
        {
            const string sql = @"
                SELECT tenant_id AS TenantId, capacity_enabled AS CapacityEnabled,
                       base_prep_minutes AS BasePrepMinutes, max_active_orders AS MaxActiveOrders,
                       show_quote_times AS ShowQuoteTimes, online_paused AS OnlinePaused, updated_at AS UpdatedAt
                FROM concession_ordering_capacity WHERE tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ConcessionOrderingCapacity>(sql, new { tenantId })).FirstOrDefault();
        }

        // Saves the admin-editable capacity config; preserves the live online_paused flag (toggled
        // separately from the cook/cashier screen).
        public async Task UpsertOrderingCapacity(ConcessionOrderingCapacity c)
        {
            const string sql = @"
                INSERT INTO concession_ordering_capacity
                    (tenant_id, capacity_enabled, base_prep_minutes, max_active_orders, show_quote_times, updated_at)
                VALUES (@TenantId, @CapacityEnabled, @BasePrepMinutes, @MaxActiveOrders, @ShowQuoteTimes, now())
                ON CONFLICT (tenant_id) DO UPDATE SET
                    capacity_enabled = @CapacityEnabled, base_prep_minutes = @BasePrepMinutes,
                    max_active_orders = @MaxActiveOrders, show_quote_times = @ShowQuoteTimes, updated_at = now()";
            await _db.Execute(sql, c);
        }

        public async Task SetOnlinePaused(Guid tenantId, bool paused)
        {
            // Upsert so the toggle works even before the admin has saved a capacity config.
            const string sql = @"
                INSERT INTO concession_ordering_capacity (tenant_id, online_paused, updated_at)
                VALUES (@tenantId, @paused, now())
                ON CONFLICT (tenant_id) DO UPDATE SET online_paused = @paused, updated_at = now()";
            await _db.Execute(sql, new { tenantId, paused });
        }

        // Active orders in the kitchen right now (paid + not yet completed), counter and online both.
        public async Task<int> CountActiveOrders(Guid tenantId)
        {
            const string sql = @"
                SELECT COUNT(*)::int FROM concession_sale
                WHERE tenant_id = @tenantId AND status = 'paid' AND fulfillment_status = 'active'";
            return (await _db.Query<int>(sql, new { tenantId })).First();
        }

        // Unfinished prep lines across active orders: the backlog the quote scales with.
        public async Task<int> CountActivePrepLines(Guid tenantId)
        {
            const string sql = @"
                SELECT COUNT(*)::int
                FROM concession_sale_line l
                JOIN concession_sale s ON s.id = l.sale_id
                WHERE s.tenant_id = @tenantId AND s.status = 'paid' AND s.fulfillment_status = 'active'
                  AND l.prep_status IN ('queued', 'in_progress')";
            return (await _db.Query<int>(sql, new { tenantId })).First();
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

        // Sold counts for SIMPLE (no-variant) products: only count lines with no variant so variant
        // stock and product stock never double-count. Reserves both pending and paid, like variants.
        public async Task<Dictionary<Guid, int>> SumSoldProducts(IEnumerable<Guid> productIds)
        {
            var ids = productIds.ToArray();
            if (ids.Length == 0) return new();
            const string sql = @"
                SELECT l.product_id AS ProductId, COALESCE(SUM(l.quantity), 0)::int AS Sold
                FROM concession_sale_line l
                JOIN concession_sale s ON s.id = l.sale_id
                WHERE l.product_id = ANY(@ids) AND l.variant_id IS NULL AND s.status IN ('pending', 'paid')
                GROUP BY l.product_id";
            var rows = await _db.Query<(Guid ProductId, int Sold)>(sql, new { ids });
            return rows.ToDictionary(r => r.ProductId, r => r.Sold);
        }

        public async Task<int> SumSoldProduct(Guid productId)
        {
            const string sql = @"
                SELECT COALESCE(SUM(l.quantity), 0)::int
                FROM concession_sale_line l
                JOIN concession_sale s ON s.id = l.sale_id
                WHERE l.product_id = @productId AND l.variant_id IS NULL AND s.status IN ('pending', 'paid')";
            return (await _db.Query<int>(sql, new { productId })).FirstOrDefault();
        }

        // Manual 86: set sold_out_date to today's business date to mark unavailable for the day, or NULL
        // to clear. Tenant-scoped so one tenant can't 86 another's item.
        public async Task SetProductSoldOut(Guid id, Guid tenantId, DateTime? soldOutDate)
        {
            const string sql = @"UPDATE concession_product SET sold_out_date = @soldOutDate, updated_at = now()
                                 WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, soldOutDate });
        }

        // ── Sales ─────────────────────────────────────────────────────────────────
        public async Task<Guid> CreateSale(ConcessionSale sale)
        {
            const string sql = @"
                INSERT INTO concession_sale
                    (tenant_id, status, fulfillment_status, order_number, subtotal_cents, tip_cents, tax_cents,
                     prices_include_tax, discount_cents, discount_kind, discount_label,
                     comp_reason_id, comp_reason_label, authorized_by_user_id, authorized_by_name, total_cents,
                     payment_method, stripe_payment_intent_id, stripe_connected_account_id, sold_by_user_id, paid_at,
                     order_channel, purchaser_user_id, purchaser_email, purchaser_name)
                VALUES (@TenantId, @Status, @FulfillmentStatus, @OrderNumber, @SubtotalCents, @TipCents, @TaxCents,
                     @PricesIncludeTax, @DiscountCents, @DiscountKind, @DiscountLabel,
                     @CompReasonId, @CompReasonLabel, @AuthorizedByUserId, @AuthorizedByName, @TotalCents,
                     @PaymentMethod, @StripePaymentIntentId, @StripeConnectedAccountId, @SoldByUserId, @PaidAt,
                     @OrderChannel, @PurchaserUserId, @PurchaserEmail, @PurchaserName)
                RETURNING id";
            return (await _db.Query<Guid>(sql, sale)).First();
        }

        // A rider's own recent concession orders (online channel), newest first, for the status view.
        public async Task<List<ConcessionSale>> ListOrdersForPurchaser(Guid tenantId, Guid userId, int take = 20)
        {
            var sql = $@"SELECT {SaleCols} FROM concession_sale
                        WHERE tenant_id = @tenantId AND purchaser_user_id = @userId
                        ORDER BY created_at DESC LIMIT @take";
            return (await _db.Query<ConcessionSale>(sql, new { tenantId, userId, take })).ToList();
        }

        public async Task CreateSaleLines(Guid saleId, IEnumerable<ConcessionSaleLine> lines)
        {
            const string lineSql = @"
                INSERT INTO concession_sale_line
                    (sale_id, product_id, variant_id, station_id, name_snapshot, variant_label,
                     unit_price_cents, quantity, line_total_cents, discount_cents, discount_kind, discount_label,
                     tax_cents, tax_rate_bps,
                     prep_status, notes, parent_line_id, is_combo, combo_tier)
                VALUES (@SaleId, @ProductId, @VariantId, @StationId, @NameSnapshot, @VariantLabel,
                     @UnitPriceCents, @Quantity, @LineTotalCents, @DiscountCents, @DiscountKind, @DiscountLabel,
                     @TaxCents, @TaxRateBps,
                     @PrepStatus, @Notes, @ParentLineId, @IsCombo, @ComboTier)
                RETURNING id";
            const string modSql = @"
                INSERT INTO concession_sale_line_modifier
                    (sale_line_id, modifier_option_id, group_name_snapshot, option_name_snapshot, price_delta_cents_snapshot)
                VALUES (@SaleLineId, @ModifierOptionId, @GroupNameSnapshot, @OptionNameSnapshot, @PriceDeltaCentsSnapshot)";
            async Task InsertOne(ConcessionSaleLine line)
            {
                line.SaleId = saleId;
                line.Id = (await _db.Query<Guid>(lineSql, line)).First();
                foreach (var mod in line.Modifiers)
                {
                    mod.SaleLineId = line.Id;
                    await _db.Execute(modSql, mod);
                }
            }
            foreach (var line in lines)
            {
                await InsertOne(line);
                // A combo parent's component children are persisted right after it, pointing back at it.
                foreach (var child in line.Children)
                {
                    child.SaleId = saleId;
                    child.ParentLineId = line.Id;
                    await InsertOne(child);
                }
            }
        }

        // Atomically assign the next per-tenant, per-day order number. The business date is the tenant's
        // LOCAL date (derived from the tenant's stored timezone), so the counter resets at local midnight,
        // not UTC midnight (which for a non-UTC track would reset mid-service and collide numbers).
        public async Task<int> NextOrderNumber(Guid tenantId)
        {
            const string sql = @"
                INSERT INTO concession_order_counter (tenant_id, business_date, last_number)
                SELECT @tenantId, (now() AT TIME ZONE COALESCE(NULLIF(t.timezone, ''), 'UTC'))::date, 1
                FROM tenant t WHERE t.id = @tenantId
                ON CONFLICT (tenant_id, business_date)
                DO UPDATE SET last_number = concession_order_counter.last_number + 1
                RETURNING last_number";
            return (await _db.Query<int>(sql, new { tenantId })).First();
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

        public async Task SetOrderNumber(Guid saleId, int orderNumber)
        {
            const string sql = @"UPDATE concession_sale SET order_number = @orderNumber
                                 WHERE id = @saleId AND order_number IS NULL";
            await _db.Execute(sql, new { saleId, orderNumber });
        }

        public async Task MarkSaleRefunded(Guid saleId, Guid tenantId)
        {
            const string sql = @"UPDATE concession_sale SET status = 'refunded'
                                 WHERE id = @saleId AND tenant_id = @tenantId AND status = 'paid'";
            await _db.Execute(sql, new { saleId, tenantId });
        }

        // Releases inventory held by abandoned card sales (reader cancelled / customer walked off):
        // a pending sale older than the cutoff is failed so SumSold stops counting it. Returns count.
        public async Task<int> FailStalePendingSales(DateTime olderThanUtc)
        {
            const string sql = @"
                UPDATE concession_sale SET status = 'failed'
                WHERE status = 'pending' AND created_at < @olderThanUtc";
            return await _db.Execute(sql, new { olderThanUtc });
        }

        // ── Sale lines + modifiers (receipt / refund / kitchen hydration) ───────────
        public async Task<List<ConcessionSaleLine>> GetSaleLines(Guid saleId)
        {
            var lines = (await _db.Query<ConcessionSaleLine>(
                $"SELECT {SaleLineCols} FROM concession_sale_line WHERE sale_id = @saleId ORDER BY id",
                new { saleId })).ToList();
            await HydrateModifiers(lines);
            return lines;
        }

        private async Task HydrateModifiers(List<ConcessionSaleLine> lines)
        {
            if (lines.Count == 0) return;
            var lineIds = lines.Select(l => l.Id).ToArray();
            const string sql = @"
                SELECT id, sale_line_id AS SaleLineId, modifier_option_id AS ModifierOptionId,
                       group_name_snapshot AS GroupNameSnapshot, option_name_snapshot AS OptionNameSnapshot,
                       price_delta_cents_snapshot AS PriceDeltaCentsSnapshot
                FROM concession_sale_line_modifier
                WHERE sale_line_id = ANY(@lineIds) ORDER BY id";
            var mods = await _db.Query<ConcessionSaleLineModifier>(sql, new { lineIds });
            var byLine = mods.GroupBy(m => m.SaleLineId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var line in lines)
                if (byLine.TryGetValue(line.Id, out var ms)) line.Modifiers = ms;
        }

        // ── Kitchen ─────────────────────────────────────────────────────────────────
        // Paid, not-yet-completed orders for the cook screen, optionally one station's lines only.
        public async Task<List<ConcessionSale>> GetKitchenSales(Guid tenantId)
        {
            var sql = $@"SELECT {SaleCols} FROM concession_sale
                        WHERE tenant_id = @tenantId AND status = 'paid' AND fulfillment_status <> 'completed'
                        ORDER BY is_rush DESC, order_number, created_at";
            return (await _db.Query<ConcessionSale>(sql, new { tenantId })).ToList();
        }

        public async Task<List<ConcessionSaleLine>> GetKitchenLines(Guid tenantId, Guid? stationId)
        {
            var stationFilter = stationId.HasValue ? "AND l.station_id = @stationId" : "";
            var sql = $@"
                SELECT l.id, l.sale_id AS SaleId, l.product_id AS ProductId, l.variant_id AS VariantId,
                       l.station_id AS StationId, l.name_snapshot AS NameSnapshot, l.variant_label AS VariantLabel,
                       l.unit_price_cents AS UnitPriceCents, l.quantity, l.line_total_cents AS LineTotalCents,
                       l.prep_status AS PrepStatus, l.notes, l.parent_line_id AS ParentLineId,
                       l.is_combo AS IsCombo, l.combo_tier AS ComboTier
                FROM concession_sale_line l
                JOIN concession_sale s ON s.id = l.sale_id
                WHERE s.tenant_id = @tenantId AND s.status = 'paid' AND s.fulfillment_status <> 'completed'
                  {stationFilter}
                ORDER BY s.order_number, l.id";
            var lines = (await _db.Query<ConcessionSaleLine>(sql, new { tenantId, stationId })).ToList();
            await HydrateModifiers(lines);
            return lines;
        }

        // Advance a single line's prep state, tenant-scoped via its sale.
        public async Task<bool> AdvanceLinePrep(Guid lineId, Guid tenantId, string prepStatus)
        {
            const string sql = @"
                UPDATE concession_sale_line l SET prep_status = @prepStatus
                FROM concession_sale s
                WHERE l.id = @lineId AND s.id = l.sale_id AND s.tenant_id = @tenantId";
            return await _db.Execute(sql, new { lineId, tenantId, prepStatus }) > 0;
        }

        // After a line changes, move the order to 'ready' once every line is ready (and not completed).
        public async Task RecomputeSaleFulfillment(Guid saleId, Guid tenantId)
        {
            const string sql = @"
                UPDATE concession_sale s
                SET fulfillment_status = CASE WHEN allReady THEN 'ready' ELSE 'active' END,
                    ready_at = CASE WHEN allReady AND s.ready_at IS NULL THEN now() ELSE s.ready_at END
                FROM (SELECT NOT EXISTS (SELECT 1 FROM concession_sale_line l
                                         WHERE l.sale_id = @saleId AND l.prep_status <> 'ready') AS allReady) x
                WHERE s.id = @saleId AND s.tenant_id = @tenantId AND s.fulfillment_status <> 'completed'";
            await _db.Execute(sql, new { saleId, tenantId });
        }

        // Claims the one-shot "order ready" SMS for this sale: sets ready_notified_at only if the order
        // is ready and not already notified. Returns true to exactly one caller, so the text sends once.
        public async Task<bool> TryMarkReadyNotified(Guid saleId, Guid tenantId)
        {
            const string sql = @"
                UPDATE concession_sale SET ready_notified_at = now()
                WHERE id = @saleId AND tenant_id = @tenantId
                  AND fulfillment_status = 'ready' AND ready_notified_at IS NULL";
            return await _db.Execute(sql, new { saleId, tenantId }) > 0;
        }

        public async Task MarkSaleCompleted(Guid saleId, Guid tenantId)
        {
            const string sql = @"UPDATE concession_sale SET fulfillment_status = 'completed', completed_at = now()
                                 WHERE id = @saleId AND tenant_id = @tenantId";
            await _db.Execute(sql, new { saleId, tenantId });
        }

        // Bring a completed order back onto the cook screen: clear completion and recompute its state.
        public async Task RecallSale(Guid saleId, Guid tenantId)
        {
            const string sql = @"
                UPDATE concession_sale s
                SET completed_at = NULL,
                    fulfillment_status = CASE WHEN NOT EXISTS (SELECT 1 FROM concession_sale_line l
                        WHERE l.sale_id = s.id AND l.prep_status <> 'ready') THEN 'ready' ELSE 'active' END
                WHERE s.id = @saleId AND s.tenant_id = @tenantId AND s.fulfillment_status = 'completed'";
            await _db.Execute(sql, new { saleId, tenantId });
        }

        // Recently completed orders (for the recall picker), newest first.
        public async Task<List<ConcessionSale>> ListRecentlyCompleted(Guid tenantId, int take = 15)
        {
            var sql = $@"SELECT {SaleCols} FROM concession_sale
                        WHERE tenant_id = @tenantId AND fulfillment_status = 'completed' AND completed_at IS NOT NULL
                        ORDER BY completed_at DESC LIMIT @take";
            return (await _db.Query<ConcessionSale>(sql, new { tenantId, take })).ToList();
        }

        // Order history for staff (cashiers + cooks): real orders (paid or refunded), newest first,
        // within an optional [fromUtc, toUtc) created-at window. An empty query returns all in range;
        // a query matches the order number, buyer name, or email.
        public async Task<List<ConcessionSale>> SearchSales(Guid tenantId, string? query, DateTime? fromUtc, DateTime? toUtc, int take = 200)
        {
            var hasQuery = !string.IsNullOrWhiteSpace(query);
            var like = hasQuery ? $"%{query!.Trim()}%" : null;
            int.TryParse(query?.Trim(), out var num);   // 0 = not a numeric query (order numbers start at 1)
            var sql = $@"
                SELECT {SaleCols} FROM concession_sale
                WHERE tenant_id = @tenantId
                  AND status IN ('paid', 'refunded')
                  AND (@fromUtc IS NULL OR created_at >= @fromUtc)
                  AND (@toUtc IS NULL OR created_at < @toUtc)
                  AND (@hasQuery = false
                       OR (@num <> 0 AND order_number = @num)
                       OR LOWER(purchaser_name) LIKE LOWER(@like)
                       OR LOWER(purchaser_email) LIKE LOWER(@like))
                ORDER BY created_at DESC
                LIMIT @take";
            return (await _db.Query<ConcessionSale>(sql, new { tenantId, fromUtc, toUtc, hasQuery, like, num, take })).ToList();
        }

        // Toggle the rush/priority flag on an order (cook screen sorts rush first).
        public async Task SetRush(Guid saleId, Guid tenantId, bool isRush)
        {
            const string sql = "UPDATE concession_sale SET is_rush = @isRush WHERE id = @saleId AND tenant_id = @tenantId";
            await _db.Execute(sql, new { saleId, tenantId, isRush });
        }

        // Kitchen prep stats for completed orders since a cutoff (computed against created_at = submitted).
        public async Task<(int Count, double AvgPrepSeconds)> GetKitchenStats(Guid tenantId, DateTime sinceUtc)
        {
            const string sql = @"
                SELECT COUNT(*)::int AS Count,
                       COALESCE(AVG(EXTRACT(EPOCH FROM (completed_at - created_at))), 0) AS AvgPrepSeconds
                FROM concession_sale
                WHERE tenant_id = @tenantId AND completed_at IS NOT NULL AND completed_at >= @sinceUtc";
            return (await _db.Query<(int Count, double AvgPrepSeconds)>(sql, new { tenantId, sinceUtc })).FirstOrDefault();
        }

        // ── Stations ──────────────────────────────────────────────────────────────
        public async Task<List<ConcessionStation>> ListStations(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? "AND is_active = true" : "";
            var sql = $@"SELECT {StationCols} FROM concession_station
                        WHERE tenant_id = @tenantId {filter} ORDER BY sort_order, LOWER(name)";
            return (await _db.Query<ConcessionStation>(sql, new { tenantId })).ToList();
        }

        public async Task<Guid> CreateStation(ConcessionStation s)
        {
            const string sql = @"INSERT INTO concession_station (tenant_id, name, sort_order, is_active)
                                 VALUES (@TenantId, @Name, @SortOrder, @IsActive) RETURNING id";
            return (await _db.Query<Guid>(sql, s)).First();
        }

        public async Task UpdateStation(ConcessionStation s)
        {
            const string sql = @"UPDATE concession_station SET name = @Name, sort_order = @SortOrder, is_active = @IsActive
                                 WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, s);
        }

        public async Task DeleteStation(Guid id, Guid tenantId)
        {
            await _db.Execute("DELETE FROM concession_station WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId });
        }

        // ── Modifier groups + options ───────────────────────────────────────────────
        public async Task<List<ConcessionModifierGroup>> ListModifierGroups(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? "AND is_active = true" : "";
            var sql = $@"SELECT {ModGroupCols} FROM concession_modifier_group
                        WHERE tenant_id = @tenantId {filter} ORDER BY sort_order, LOWER(name)";
            return (await _db.Query<ConcessionModifierGroup>(sql, new { tenantId })).ToList();
        }

        // Seed an editable starter catalog (categories, stations, modifier groups, and a few sample
        // products with sensible wiring) so a tenant turning concessions on has a working menu to edit
        // instead of a blank slate. Idempotent by NAME: each item is created only if one with the same
        // name doesn't already exist, so it never duplicates and is safe to run again to fill gaps.
        // When onlyIfEmpty is true (the auto first-enable path) it no-ops if any catalog already exists,
        // so it won't re-add things an admin deliberately deleted; the manual "Load starter" button
        // passes false to fill in whatever's missing.
        public async Task SeedStarterCatalog(Guid tenantId, bool onlyIfEmpty)
        {
            var products = await ListProducts(tenantId, activeOnly: false);
            var categories = await ListCategories(tenantId, activeOnly: false);
            var groups = await ListModifierGroups(tenantId, activeOnly: false);
            var stations = await ListStations(tenantId, activeOnly: false);

            if (onlyIfEmpty && (products.Count > 0 || categories.Count > 0 || groups.Count > 0 || stations.Count > 0))
                return;

            // Categories
            var catId = categories.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
            var catNames = new[] { "Burgers & Dogs", "Sides", "Snacks", "Drinks" };
            for (var i = 0; i < catNames.Length; i++)
                if (!catId.ContainsKey(catNames[i]))
                    catId[catNames[i]] = await CreateCategory(new ConcessionCategory
                    { TenantId = tenantId, Name = catNames[i], SortOrder = i * 10, IsActive = true });

            // Stations
            var stnId = stations.ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);
            var stnNames = new[] { "Grill", "Fryer", "Drinks" };
            for (var i = 0; i < stnNames.Length; i++)
                if (!stnId.ContainsKey(stnNames[i]))
                    stnId[stnNames[i]] = await CreateStation(new ConcessionStation
                    { TenantId = tenantId, Name = stnNames[i], SortOrder = i * 10, IsActive = true });

            // Modifier groups + options. Positive "Toppings" (selected = on the item) so defaults can put
            // lettuce/tomato on a burger and the customer simply unchecks to remove.
            var grpId = groups.ToDictionary(g => g.Name, g => g.Id, StringComparer.OrdinalIgnoreCase);
            var optIds = new Dictionary<string, Dictionary<string, Guid>>(StringComparer.OrdinalIgnoreCase);  // group -> (option -> id)
            async Task EnsureGroup(string name, int sort, (string Name, int Delta)[] opts)
            {
                if (grpId.TryGetValue(name, out var existingGid))
                {
                    // Already present (idempotent): capture its option ids so product defaults still resolve.
                    var existing = await ListOptionsForGroups(new[] { existingGid }, activeOnly: false);
                    optIds[name] = existing.ToDictionary(o => o.Name, o => o.Id, StringComparer.OrdinalIgnoreCase);
                    return;
                }
                var gid = await CreateModifierGroup(new ConcessionModifierGroup
                { TenantId = tenantId, Name = name, MinSelect = 0, MaxSelect = null, IsRequired = false, SortOrder = sort, IsActive = true });
                var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < opts.Length; i++)
                    map[opts[i].Name] = await CreateOption(new ConcessionModifierOption
                    { GroupId = gid, Name = opts[i].Name, PriceDeltaCents = opts[i].Delta, SortOrder = i, IsActive = true });
                grpId[name] = gid;
                optIds[name] = map;
            }
            await EnsureGroup("Toppings", 0, new[] { ("Lettuce", 0), ("Tomato", 0), ("Onion", 0), ("Pickle", 0) });
            await EnsureGroup("Condiments", 1, new[] { ("Ketchup", 0), ("Mustard", 0), ("Mayo", 0), ("Relish", 0), ("BBQ sauce", 0) });
            await EnsureGroup("Add-ons", 2, new[] { ("Extra cheese", 100), ("Bacon", 150), ("Avocado", 150), ("Extra patty", 250) });

            // Inventory items (ingredients/goods), idempotent by name. Costs/on-hand are starter defaults.
            var existingInv = await ListInventoryItems(tenantId, activeOnly: false);
            var invId = existingInv.ToDictionary(i => i.Name, i => i.Id, StringComparer.OrdinalIgnoreCase);
            async Task EnsureInventoryItem(string name, string unit, int costCents, decimal onHand)
            {
                if (invId.ContainsKey(name)) return;
                invId[name] = await CreateInventoryItem(new ConcessionInventoryItem
                { TenantId = tenantId, Name = name, Unit = unit, CostCents = costCents, OnHand = onHand, IsActive = true });
            }
            await EnsureInventoryItem("Hamburger bun", "each", 30, 200);
            await EnsureInventoryItem("Hot dog bun", "each", 25, 200);
            await EnsureInventoryItem("Beef patty", "each", 95, 200);
            await EnsureInventoryItem("Hot dog", "each", 45, 200);
            await EnsureInventoryItem("Cheese slice", "each", 15, 300);
            await EnsureInventoryItem("Lettuce", "oz", 8, 400);
            await EnsureInventoryItem("Tomato", "oz", 10, 400);
            await EnsureInventoryItem("French fries", "serving", 35, 300);
            await EnsureInventoryItem("Onion rings", "serving", 45, 150);
            await EnsureInventoryItem("Chicken tenders", "serving", 110, 150);
            await EnsureInventoryItem("Chips bag", "each", 40, 100);
            await EnsureInventoryItem("Candy bar", "each", 60, 100);
            await EnsureInventoryItem("Bottled water", "each", 30, 200);
            await EnsureInventoryItem("Soda cup", "each", 20, 300);

            // Sample products (only if a product with the same name doesn't already exist), with sensible
            // default selections (e.g. a cheeseburger comes with lettuce, tomato, ketchup) and a starter recipe.
            var existingNames = products.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var prodId = products.ToDictionary(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase);
            var sort = 0;
            async Task EnsureProduct(string name, int priceCents, string category, string? station, string[] mods, string[] defaults,
                (string Item, decimal Qty)[]? recipe = null, bool comboAvailable = false, string? imageUrl = null)
            {
                if (existingNames.Contains(name)) return;
                var p = new ConcessionProduct
                {
                    TenantId = tenantId,
                    Name = name,
                    PriceCents = priceCents,
                    CategoryId = catId.TryGetValue(category, out var cid) ? cid : (Guid?)null,
                    StationId = station != null && stnId.TryGetValue(station, out var sid) ? sid : (Guid?)null,
                    ImageUrl = imageUrl,
                    IsActive = true,
                    ShowInCarousel = true,
                    ComboAvailable = comboAvailable,
                    SortOrder = sort += 10,
                };
                p.Id = await CreateProduct(p);
                prodId[name] = p.Id;
                var gids = mods.Where(m => grpId.ContainsKey(m)).Select(m => grpId[m]).ToList();
                if (gids.Count > 0) await SetProductGroups(p.Id, gids);
                // Resolve default option names within this product's own groups.
                var defIds = new List<Guid>();
                foreach (var dn in defaults)
                    foreach (var m in mods)
                        if (optIds.TryGetValue(m, out var om) && om.TryGetValue(dn, out var oid)) { defIds.Add(oid); break; }
                if (defIds.Count > 0) await SetProductDefaultOptions(p.Id, defIds);
                // Starter recipe: resolve item names to ids (skip any not seeded) and store the depletion config.
                if (recipe is { Length: > 0 })
                {
                    var lines = recipe
                        .Where(r => invId.ContainsKey(r.Item))
                        .Select(r => (ItemId: invId[r.Item], Quantity: r.Qty))
                        .ToList();
                    if (lines.Count > 0) await SetRecipe(p.Id, lines);
                }
                existingNames.Add(name);
            }
            // Hand-picked Unsplash photos (Unsplash License: royalty-free, commercial, no attribution).
            const string cheeseburgerImg = "https://images.unsplash.com/photo-1534790566855-4cb788d389ec?w=600&h=400&fit=crop&q=80";
            const string hotDogImg = "https://images.unsplash.com/photo-1721648371118-ce2fb35e66a8?w=600&h=400&fit=crop&q=80";
            const string friesImg = "https://images.unsplash.com/photo-1615485290836-4ebcebf44aaf?w=600&h=400&fit=crop&q=80";
            const string waterImg = "https://images.unsplash.com/photo-1550505095-81378a674395?w=600&h=400&fit=crop&q=80";
            const string onionRingsImg = "https://images.unsplash.com/photo-1630825533949-74f62f54553a?w=600&h=400&fit=crop&q=80";
            const string tendersImg = "https://images.unsplash.com/photo-1605291581926-df4bf7ee3e89?w=600&h=400&fit=crop&q=80";
            const string chipsImg = "https://images.unsplash.com/photo-1528751014936-863e6e7a319c?w=600&h=400&fit=crop&q=80";
            const string candyImg = "https://images.unsplash.com/photo-1522249341405-3871994ac062?w=600&h=400&fit=crop&q=80";
            const string sodaImg = "https://images.unsplash.com/photo-1655604646117-999cfb2bba37?w=600&h=400&fit=crop&q=80";

            await EnsureProduct("Cheeseburger", 700, "Burgers & Dogs", "Grill",
                new[] { "Toppings", "Condiments", "Add-ons" }, new[] { "Lettuce", "Tomato", "Ketchup" },
                new[] { ("Hamburger bun", 1m), ("Beef patty", 1m), ("Cheese slice", 1m), ("Lettuce", 0.5m), ("Tomato", 0.5m) },
                comboAvailable: true, imageUrl: cheeseburgerImg);
            await EnsureProduct("Hot Dog", 500, "Burgers & Dogs", "Grill",
                new[] { "Toppings", "Condiments" }, new[] { "Ketchup", "Mustard" },
                new[] { ("Hot dog bun", 1m), ("Hot dog", 1m) },
                comboAvailable: true, imageUrl: hotDogImg);
            await EnsureProduct("French Fries", 400, "Sides", "Fryer", new[] { "Condiments" }, Array.Empty<string>(),
                new[] { ("French fries", 1m) }, imageUrl: friesImg);
            await EnsureProduct("Onion Rings", 500, "Sides", "Fryer", new[] { "Condiments" }, Array.Empty<string>(),
                new[] { ("Onion rings", 1m) }, imageUrl: onionRingsImg);
            await EnsureProduct("Chicken Tenders", 750, "Sides", "Fryer", new[] { "Condiments" }, Array.Empty<string>(),
                new[] { ("Chicken tenders", 1m) }, comboAvailable: true, imageUrl: tendersImg);
            await EnsureProduct("Chips", 150, "Snacks", null, Array.Empty<string>(), Array.Empty<string>(),
                new[] { ("Chips bag", 1m) }, imageUrl: chipsImg);
            await EnsureProduct("Candy Bar", 200, "Snacks", null, Array.Empty<string>(), Array.Empty<string>(),
                new[] { ("Candy bar", 1m) }, imageUrl: candyImg);
            await EnsureProduct("Bottled Water", 200, "Drinks", "Drinks", Array.Empty<string>(), Array.Empty<string>(),
                new[] { ("Bottled water", 1m) }, imageUrl: waterImg);
            await EnsureProduct("Soda", 250, "Drinks", "Drinks", Array.Empty<string>(), Array.Empty<string>(),
                new[] { ("Soda cup", 1m) }, imageUrl: sodaImg);

            // Size variants matched to the combo tiers (idempotent by size), so a side/drink resolves to
            // the right size at each tier and substitutions are priced at that size.
            async Task EnsureVariant(string productName, string size, int priceCents)
            {
                if (!prodId.TryGetValue(productName, out var pid)) return;
                var existing = await ListVariants(pid);
                if (existing.Any(v => string.Equals(v.Size, size, StringComparison.OrdinalIgnoreCase))) return;
                await CreateVariant(new ConcessionVariant
                { ProductId = pid, Size = size, PriceCents = priceCents, IsActive = true, SortOrder = existing.Count });
            }
            foreach (var (name, reg, lg, xl) in new[]
            {
                ("French Fries", 400, 550, 650),
                ("Onion Rings", 500, 650, 750),
                ("Soda", 250, 350, 450),
            })
            {
                await EnsureVariant(name, "Regular", reg);
                await EnsureVariant(name, "Large", lg);
                await EnsureVariant(name, "Extra Large", xl);
            }

            // Shared "make it a combo" definition: size tiers + side/drink slots. Seed once (only if the
            // tenant has no tiers yet) so re-running starter content doesn't duplicate it.
            if ((await GetComboTiers(tenantId)).Count == 0)
            {
                await SetComboTiers(tenantId, new[]
                {
                    new ConcessionComboTier { Name = "Regular", SizeLabel = "Regular", PriceCents = 300 },
                    new ConcessionComboTier { Name = "Large", SizeLabel = "Large", PriceCents = 450 },
                    new ConcessionComboTier { Name = "Extra Large", SizeLabel = "Extra Large", PriceCents = 600 },
                });

                var slots = new List<ConcessionComboSlot>();
                var side = new ConcessionComboSlot { Name = "Choose a side", IsRequired = true };
                if (prodId.TryGetValue("French Fries", out var frId))
                    side.Options.Add(new ConcessionComboSlotOption { ComponentProductId = frId, IsDefault = true });
                if (prodId.TryGetValue("Onion Rings", out var orId))
                    side.Options.Add(new ConcessionComboSlotOption { ComponentProductId = orId });
                if (side.Options.Count > 0) slots.Add(side);

                var drink = new ConcessionComboSlot { Name = "Choose a drink", IsRequired = true };
                if (prodId.TryGetValue("Soda", out var sdId))
                    drink.Options.Add(new ConcessionComboSlotOption { ComponentProductId = sdId, IsDefault = true });
                if (prodId.TryGetValue("Bottled Water", out var bwId))
                    drink.Options.Add(new ConcessionComboSlotOption { ComponentProductId = bwId });
                if (drink.Options.Count > 0) slots.Add(drink);

                if (slots.Count > 0) await SetComboSlots(tenantId, slots);
            }
        }

        public async Task<ConcessionModifierGroup?> GetModifierGroup(Guid id, Guid tenantId)
        {
            var sql = $@"SELECT {ModGroupCols} FROM concession_modifier_group
                        WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ConcessionModifierGroup>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateModifierGroup(ConcessionModifierGroup g)
        {
            const string sql = @"
                INSERT INTO concession_modifier_group
                    (tenant_id, name, min_select, max_select, is_required, sort_order, is_active)
                VALUES (@TenantId, @Name, @MinSelect, @MaxSelect, @IsRequired, @SortOrder, @IsActive)
                RETURNING id";
            return (await _db.Query<Guid>(sql, g)).First();
        }

        public async Task UpdateModifierGroup(ConcessionModifierGroup g)
        {
            const string sql = @"
                UPDATE concession_modifier_group SET
                    name = @Name, min_select = @MinSelect, max_select = @MaxSelect,
                    is_required = @IsRequired, sort_order = @SortOrder, is_active = @IsActive
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, g);
        }

        public async Task DeleteModifierGroup(Guid id, Guid tenantId)
        {
            await _db.Execute("DELETE FROM concession_modifier_group WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId });
        }

        public async Task<List<ConcessionModifierOption>> ListOptionsForGroups(IEnumerable<Guid> groupIds, bool activeOnly)
        {
            var ids = groupIds.ToArray();
            if (ids.Length == 0) return new();
            var filter = activeOnly ? "AND is_active = true" : "";
            var sql = $@"SELECT {ModOptionCols} FROM concession_modifier_option
                        WHERE group_id = ANY(@ids) {filter} ORDER BY sort_order, id";
            return (await _db.Query<ConcessionModifierOption>(sql, new { ids })).ToList();
        }

        // Names for a set of option ids (for showing removed defaults on the cook screen).
        public async Task<Dictionary<Guid, string>> GetOptionNames(IEnumerable<Guid> optionIds)
        {
            var ids = optionIds.Distinct().ToArray();
            if (ids.Length == 0) return new();
            var rows = await _db.Query<(Guid Id, string Name)>(
                "SELECT id AS Id, name AS Name FROM concession_modifier_option WHERE id = ANY(@ids)", new { ids });
            return rows.ToDictionary(r => r.Id, r => r.Name);
        }

        public async Task<ConcessionModifierOption?> GetOption(Guid id)
        {
            var sql = $"SELECT {ModOptionCols} FROM concession_modifier_option WHERE id = @id LIMIT 1";
            return (await _db.Query<ConcessionModifierOption>(sql, new { id })).FirstOrDefault();
        }

        // Replace a group's options in one shot (used by the admin editor). Tenant-scoped via the group.
        public async Task<Guid> CreateOption(ConcessionModifierOption o)
        {
            const string sql = @"
                INSERT INTO concession_modifier_option (group_id, name, price_delta_cents, sort_order, is_active)
                VALUES (@GroupId, @Name, @PriceDeltaCents, @SortOrder, @IsActive) RETURNING id";
            return (await _db.Query<Guid>(sql, o)).First();
        }

        public async Task UpdateOption(ConcessionModifierOption o)
        {
            const string sql = @"UPDATE concession_modifier_option SET
                    name = @Name, price_delta_cents = @PriceDeltaCents, sort_order = @SortOrder, is_active = @IsActive
                WHERE id = @Id";
            await _db.Execute(sql, o);
        }

        public async Task DeleteOption(Guid id)
        {
            await _db.Execute("DELETE FROM concession_modifier_option WHERE id = @id", new { id });
        }

        // ── Product -> modifier group assignment ────────────────────────────────────
        public async Task<List<Guid>> GetProductGroupIds(Guid productId)
        {
            const string sql = @"SELECT group_id FROM concession_product_modifier_group
                                 WHERE product_id = @productId ORDER BY sort_order";
            return (await _db.Query<Guid>(sql, new { productId })).ToList();
        }

        public async Task SetProductGroups(Guid productId, IReadOnlyList<Guid> groupIds)
        {
            await _db.Execute("DELETE FROM concession_product_modifier_group WHERE product_id = @productId",
                new { productId });
            for (int i = 0; i < groupIds.Count; i++)
            {
                await _db.Execute(@"INSERT INTO concession_product_modifier_group (product_id, group_id, sort_order)
                                    VALUES (@productId, @groupId, @sortOrder)",
                    new { productId, groupId = groupIds[i], sortOrder = i });
            }
        }

        // product_id -> ordered group_ids, for hydrating the cashier catalog.
        public async Task<Dictionary<Guid, List<Guid>>> ListProductGroupLinks(IEnumerable<Guid> productIds)
        {
            var ids = productIds.ToArray();
            if (ids.Length == 0) return new();
            const string sql = @"SELECT product_id AS ProductId, group_id AS GroupId, sort_order AS SortOrder
                                 FROM concession_product_modifier_group
                                 WHERE product_id = ANY(@ids) ORDER BY sort_order";
            var rows = await _db.Query<(Guid ProductId, Guid GroupId, int SortOrder)>(sql, new { ids });
            return rows.GroupBy(r => r.ProductId)
                       .ToDictionary(g => g.Key, g => g.Select(r => r.GroupId).ToList());
        }

        // ── Product default modifier options (pre-selected on add, e.g. lettuce + tomato) ──────────
        public async Task<List<Guid>> GetProductDefaultOptionIds(Guid productId)
        {
            const string sql = "SELECT modifier_option_id FROM concession_product_default_option WHERE product_id = @productId";
            return (await _db.Query<Guid>(sql, new { productId })).ToList();
        }

        public async Task SetProductDefaultOptions(Guid productId, IReadOnlyList<Guid> optionIds)
        {
            await _db.Execute("DELETE FROM concession_product_default_option WHERE product_id = @productId",
                new { productId });
            foreach (var optionId in optionIds.Distinct())
            {
                await _db.Execute(@"INSERT INTO concession_product_default_option (product_id, modifier_option_id)
                                    VALUES (@productId, @optionId)", new { productId, optionId });
            }
        }

        // product_id -> default option_ids, for hydrating the cashier catalog.
        public async Task<Dictionary<Guid, List<Guid>>> ListProductDefaultOptionLinks(IEnumerable<Guid> productIds)
        {
            var ids = productIds.ToArray();
            if (ids.Length == 0) return new();
            const string sql = @"SELECT product_id AS ProductId, modifier_option_id AS OptionId
                                 FROM concession_product_default_option WHERE product_id = ANY(@ids)";
            var rows = await _db.Query<(Guid ProductId, Guid OptionId)>(sql, new { ids });
            return rows.GroupBy(r => r.ProductId)
                       .ToDictionary(g => g.Key, g => g.Select(r => r.OptionId).ToList());
        }

        // ── Inventory items ──────────────────────────────────────────────────────
        private const string InventoryItemCols = @"
            id, tenant_id AS TenantId, name, unit, cost_cents AS CostCents, on_hand AS OnHand,
            low_stock_threshold AS LowStockThreshold, low_stock_notified_at AS LowStockNotifiedAt,
            is_active AS IsActive, created_at AS CreatedAt, updated_at AS UpdatedAt";

        public async Task<List<ConcessionInventoryItem>> ListInventoryItems(Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? "AND is_active = true" : "";
            var sql = $@"SELECT {InventoryItemCols} FROM concession_inventory_item
                        WHERE tenant_id = @tenantId {filter} ORDER BY LOWER(name)";
            return (await _db.Query<ConcessionInventoryItem>(sql, new { tenantId })).ToList();
        }

        public async Task<ConcessionInventoryItem?> GetInventoryItem(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {InventoryItemCols} FROM concession_inventory_item WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ConcessionInventoryItem>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> CreateInventoryItem(ConcessionInventoryItem i)
        {
            const string sql = @"INSERT INTO concession_inventory_item
                    (tenant_id, name, unit, cost_cents, on_hand, low_stock_threshold, is_active)
                VALUES (@TenantId, @Name, @Unit, @CostCents, @OnHand, @LowStockThreshold, @IsActive) RETURNING id";
            return (await _db.Query<Guid>(sql, i)).First();
        }

        public async Task UpdateInventoryItem(ConcessionInventoryItem i)
        {
            // Clear the low-stock alert dedupe when the item is no longer low (threshold raised / removed,
            // or on-hand edited up), so a future dip re-alerts.
            const string sql = @"UPDATE concession_inventory_item SET name=@Name, unit=@Unit, cost_cents=@CostCents,
                on_hand=@OnHand, low_stock_threshold=@LowStockThreshold, is_active=@IsActive,
                low_stock_notified_at = CASE WHEN @LowStockThreshold IS NULL OR @OnHand > @LowStockThreshold
                                             THEN NULL ELSE low_stock_notified_at END,
                updated_at=now() WHERE id=@Id AND tenant_id=@TenantId";
            await _db.Execute(sql, i);
        }

        public async Task DeleteInventoryItem(Guid id, Guid tenantId)
        {
            await _db.Execute("DELETE FROM concession_inventory_item WHERE id=@id AND tenant_id=@tenantId", new { id, tenantId });
        }

        // Add received stock (delta to on-hand), keeping theoretical accurate between counts. Restocking
        // above the threshold clears the alert dedupe so the next low episode notifies again.
        public async Task ReceiveStock(Guid id, Guid tenantId, decimal quantity)
        {
            await _db.Execute(@"UPDATE concession_inventory_item
                SET on_hand = on_hand + @quantity, updated_at=now(),
                    low_stock_notified_at = CASE WHEN low_stock_threshold IS NULL
                                                 OR on_hand + @quantity > low_stock_threshold
                                                 THEN NULL ELSE low_stock_notified_at END
                WHERE id=@id AND tenant_id=@tenantId", new { id, tenantId, quantity });
        }

        // Atomically claims the newly-low items for this tenant (threshold set, on_hand <= threshold, not
        // yet alerted), stamping low_stock_notified_at so each low episode alerts once. Returns the rows.
        public async Task<List<ConcessionInventoryItem>> MarkAndGetNewlyLowStock(Guid tenantId)
        {
            var sql = $@"
                UPDATE concession_inventory_item
                SET low_stock_notified_at = now()
                WHERE tenant_id = @tenantId AND is_active = true
                  AND low_stock_threshold IS NOT NULL
                  AND on_hand <= low_stock_threshold
                  AND low_stock_notified_at IS NULL
                RETURNING {InventoryItemCols}";
            return (await _db.Query<ConcessionInventoryItem>(sql, new { tenantId })).ToList();
        }

        // ── Recipes ──────────────────────────────────────────────────────────────
        public async Task<List<ConcessionRecipeLine>> GetRecipe(Guid productId)
        {
            const string sql = @"SELECT r.product_id AS ProductId, r.inventory_item_id AS InventoryItemId,
                r.quantity AS Quantity, i.name AS ItemName, i.unit AS Unit
                FROM concession_recipe_item r JOIN concession_inventory_item i ON i.id = r.inventory_item_id
                WHERE r.product_id = @productId ORDER BY LOWER(i.name)";
            return (await _db.Query<ConcessionRecipeLine>(sql, new { productId })).ToList();
        }

        public async Task SetRecipe(Guid productId, IReadOnlyList<(Guid ItemId, decimal Quantity)> lines)
        {
            await _db.Execute("DELETE FROM concession_recipe_item WHERE product_id=@productId", new { productId });
            foreach (var l in lines.Where(l => l.Quantity > 0).DistinctBy(l => l.ItemId))
                await _db.Execute(@"INSERT INTO concession_recipe_item (product_id, inventory_item_id, quantity)
                    VALUES (@productId, @itemId, @qty)", new { productId, itemId = l.ItemId, qty = l.Quantity });
        }

        // ── Combos (shared, tenant-level "make it a combo" definition) ───────────────
        public async Task<List<ConcessionComboTier>> GetComboTiers(Guid tenantId)
        {
            return (await _db.Query<ConcessionComboTier>(
                @"SELECT id, tenant_id AS TenantId, name, size_label AS SizeLabel, price_cents AS PriceCents,
                         sort_order AS SortOrder
                  FROM concession_combo_tier WHERE tenant_id = @tenantId ORDER BY sort_order, id",
                new { tenantId })).ToList();
        }

        public async Task SetComboTiers(Guid tenantId, IReadOnlyList<ConcessionComboTier> tiers)
        {
            await _db.Execute("DELETE FROM concession_combo_tier WHERE tenant_id = @tenantId", new { tenantId });
            for (var i = 0; i < tiers.Count; i++)
            {
                var t = tiers[i];
                await _db.Execute(
                    @"INSERT INTO concession_combo_tier (tenant_id, name, size_label, price_cents, sort_order)
                      VALUES (@tenantId, @name, @size, @price, @sort)",
                    new { tenantId, name = t.Name, size = t.SizeLabel, price = t.PriceCents, sort = i });
            }
        }

        // Combo slots (with their options) for a tenant. Options join the component product name + station
        // (snapshotted onto the child line at sale time).
        public async Task<List<ConcessionComboSlot>> GetComboSlots(Guid tenantId)
        {
            var slots = (await _db.Query<ConcessionComboSlot>(
                @"SELECT id, tenant_id AS TenantId, name, is_required AS IsRequired, sort_order AS SortOrder
                  FROM concession_combo_slot WHERE tenant_id = @tenantId ORDER BY sort_order, id",
                new { tenantId })).ToList();
            if (slots.Count == 0) return slots;

            var slotIds = slots.Select(s => s.Id).ToArray();
            var options = await _db.Query<ConcessionComboSlotOption>(
                @"SELECT o.id, o.slot_id AS SlotId, o.component_product_id AS ComponentProductId,
                         o.is_default AS IsDefault, o.sort_order AS SortOrder,
                         p.name AS ComponentName, p.station_id AS StationId
                  FROM concession_combo_slot_option o
                  JOIN concession_product p ON p.id = o.component_product_id
                  WHERE o.slot_id = ANY(@slotIds) ORDER BY o.sort_order, o.id",
                new { slotIds });
            var bySlot = options.GroupBy(o => o.SlotId).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var s in slots)
                if (bySlot.TryGetValue(s.Id, out var opts)) s.Options = opts;
            return slots;
        }

        // Replace a tenant's combo slots + options wholesale (cascade deletes the old options).
        public async Task SetComboSlots(Guid tenantId, IReadOnlyList<ConcessionComboSlot> slots)
        {
            await _db.Execute("DELETE FROM concession_combo_slot WHERE tenant_id = @tenantId", new { tenantId });
            for (var si = 0; si < slots.Count; si++)
            {
                var s = slots[si];
                var slotId = (await _db.Query<Guid>(
                    @"INSERT INTO concession_combo_slot (tenant_id, name, is_required, sort_order)
                      VALUES (@tenantId, @name, @isRequired, @sort) RETURNING id",
                    new { tenantId, name = s.Name, isRequired = s.IsRequired, sort = si })).First();
                var opts = s.Options;
                for (var oi = 0; oi < opts.Count; oi++)
                {
                    var o = opts[oi];
                    await _db.Execute(
                        @"INSERT INTO concession_combo_slot_option (slot_id, component_product_id, is_default, sort_order)
                          VALUES (@slotId, @componentId, @isDefault, @sort)",
                        new { slotId, componentId = o.ComponentProductId, isDefault = o.IsDefault, sort = oi });
                }
            }
        }

        // Deplete theoretical inventory for a paid sale's lines via their recipes. Best-effort (the
        // caller swallows failures so it never blocks the sale).
        public async Task DepleteInventoryForSale(Guid saleId, Guid tenantId)
        {
            const string sql = @"
                UPDATE concession_inventory_item i
                SET on_hand = i.on_hand - usage.qty, updated_at = now()
                FROM (SELECT r.inventory_item_id, SUM(r.quantity * l.quantity) AS qty
                      FROM concession_sale_line l
                      JOIN concession_recipe_item r ON r.product_id = l.product_id
                      WHERE l.sale_id = @saleId
                      GROUP BY r.inventory_item_id) usage
                WHERE i.id = usage.inventory_item_id AND i.tenant_id = @tenantId";
            await _db.Execute(sql, new { saleId, tenantId });
        }

        // ── Stock takes (counts) ───────────────────────────────────────────────────
        // Records a count (expected = current on_hand snapshot, counted = entered) and reconciles on_hand
        // to the counted values. Returns the count id.
        public async Task<Guid> CreateInventoryCount(Guid tenantId, Guid? countedBy, string? note,
            IReadOnlyList<(Guid ItemId, decimal CountedQty)> lines)
        {
            var countId = (await _db.Query<Guid>(@"INSERT INTO concession_inventory_count (tenant_id, counted_by, note)
                VALUES (@tenantId, @countedBy, @note) RETURNING id", new { tenantId, countedBy, note })).First();
            foreach (var l in lines)
            {
                var item = await GetInventoryItem(l.ItemId, tenantId);
                if (item is null) continue;
                await _db.Execute(@"INSERT INTO concession_inventory_count_line
                    (count_id, inventory_item_id, name_snapshot, unit_snapshot, unit_cost_cents, expected_qty, counted_qty)
                    VALUES (@countId, @itemId, @name, @unit, @cost, @expected, @counted)",
                    new { countId, itemId = l.ItemId, name = item.Name, unit = item.Unit, cost = item.CostCents,
                          expected = item.OnHand, counted = l.CountedQty });
                await _db.Execute(@"UPDATE concession_inventory_item SET on_hand=@counted, updated_at=now(),
                        low_stock_notified_at = CASE WHEN low_stock_threshold IS NULL OR @counted > low_stock_threshold
                                                     THEN NULL ELSE low_stock_notified_at END
                    WHERE id=@itemId AND tenant_id=@tenantId",
                    new { counted = l.CountedQty, itemId = l.ItemId, tenantId });
            }
            return countId;
        }

        // Recent counts with their total variance (counted - expected) cost in cents (negative = loss).
        public async Task<List<(Guid Id, DateTime CreatedAt, string? Note, long VarianceCents)>> ListInventoryCounts(Guid tenantId, int take = 30)
        {
            const string sql = @"SELECT c.id AS Id, c.created_at AS CreatedAt, c.note AS Note,
                COALESCE(SUM((cl.counted_qty - cl.expected_qty) * cl.unit_cost_cents), 0)::bigint AS VarianceCents
                FROM concession_inventory_count c
                LEFT JOIN concession_inventory_count_line cl ON cl.count_id = c.id
                WHERE c.tenant_id = @tenantId
                GROUP BY c.id ORDER BY c.created_at DESC LIMIT @take";
            return (await _db.Query<(Guid, DateTime, string?, long)>(sql, new { tenantId, take })).ToList();
        }

        public async Task<List<ConcessionInventoryCountLine>> GetInventoryCountLines(Guid countId, Guid tenantId)
        {
            const string sql = @"SELECT cl.id, cl.count_id AS CountId, cl.inventory_item_id AS InventoryItemId,
                cl.name_snapshot AS NameSnapshot, cl.unit_snapshot AS UnitSnapshot, cl.unit_cost_cents AS UnitCostCents,
                cl.expected_qty AS ExpectedQty, cl.counted_qty AS CountedQty
                FROM concession_inventory_count_line cl
                JOIN concession_inventory_count c ON c.id = cl.count_id
                WHERE cl.count_id = @countId AND c.tenant_id = @tenantId
                ORDER BY LOWER(cl.name_snapshot)";
            return (await _db.Query<ConcessionInventoryCountLine>(sql, new { countId, tenantId })).ToList();
        }

        // ── Profitability reporting ──────────────────────────────────────────────────
        // All reporting queries cover paid sales only (status='paid'); refunded sales become status
        // 'refunded' and are reported separately as a deduction. Tenant-scoped via concession_sale.

        public async Task<ConcessionSalesAggregate> GetSalesAggregate(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            // Net sales = gross item subtotal, less any discount/comp, less any tax baked into it
            // (inclusive pricing). subtotal_cents is the pre-discount gross, so discount_cents must be
            // subtracted here for the dashboards to reconcile with the per-item/line totals.
            const string sql = @"
                SELECT COUNT(*)::int AS OrderCount,
                       COALESCE(SUM(subtotal_cents - discount_cents - CASE WHEN prices_include_tax THEN tax_cents ELSE 0 END), 0)::bigint AS NetSalesCents,
                       COALESCE(SUM(tax_cents), 0)::bigint  AS TaxCents,
                       COALESCE(SUM(tip_cents), 0)::bigint  AS TipCents,
                       COALESCE(SUM(total_cents), 0)::bigint AS TotalCents
                FROM concession_sale
                WHERE tenant_id = @tenantId AND status = 'paid'
                  AND created_at >= @fromUtc AND created_at < @toUtc";
            return (await _db.Query<ConcessionSalesAggregate>(sql, new { tenantId, fromUtc, toUtc })).First();
        }

        // Theoretical COGS = each paid line's quantity x its recipe (ingredient qty x current unit cost).
        // Combo component child lines deplete inventory too, so their recipe cost is included.
        public async Task<long> GetCogsTotal(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT COALESCE(SUM(l.quantity * ri.quantity * ii.cost_cents), 0)::bigint
                FROM concession_sale_line l
                JOIN concession_sale s ON s.id = l.sale_id
                JOIN concession_recipe_item ri ON ri.product_id = l.product_id
                JOIN concession_inventory_item ii ON ii.id = ri.inventory_item_id
                WHERE s.tenant_id = @tenantId AND s.status = 'paid'
                  AND s.created_at >= @fromUtc AND s.created_at < @toUtc";
            return (await _db.Query<long>(sql, new { tenantId, fromUtc, toUtc })).First();
        }

        public async Task<ConcessionRefundAggregate> GetRefundAggregate(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT COUNT(*)::int AS RefundedCount,
                       COALESCE(SUM(total_cents), 0)::bigint AS RefundedAmountCents
                FROM concession_sale
                WHERE tenant_id = @tenantId AND status = 'refunded'
                  AND created_at >= @fromUtc AND created_at < @toUtc";
            return (await _db.Query<ConcessionRefundAggregate>(sql, new { tenantId, fromUtc, toUtc })).First();
        }

        public async Task<List<ConcessionPaymentRow>> GetPaymentBreakdown(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT payment_method AS PaymentMethod, COUNT(*)::int AS SaleCount,
                       COALESCE(SUM(total_cents), 0)::bigint AS AmountCents
                FROM concession_sale
                WHERE tenant_id = @tenantId AND status = 'paid'
                  AND created_at >= @fromUtc AND created_at < @toUtc
                GROUP BY payment_method
                ORDER BY AmountCents DESC";
            return (await _db.Query<ConcessionPaymentRow>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<List<ConcessionItemProfit>> GetItemProfitability(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            // Revenue and COGS are aggregated separately (the recipe join fans out rows) then merged by product.
            const string sql = @"
                WITH rev AS (
                    SELECT l.product_id AS pid, MIN(l.name_snapshot) AS name,
                           SUM(l.quantity)::int AS qty,
                           SUM(l.line_total_cents - CASE WHEN s.prices_include_tax THEN l.tax_cents ELSE 0 END)::bigint AS revenue
                    FROM concession_sale_line l
                    JOIN concession_sale s ON s.id = l.sale_id
                    WHERE s.tenant_id = @tenantId AND s.status = 'paid'
                      AND s.created_at >= @fromUtc AND s.created_at < @toUtc
                    GROUP BY l.product_id
                ),
                cogs AS (
                    SELECT l.product_id AS pid, SUM(l.quantity * ri.quantity * ii.cost_cents)::bigint AS cogs
                    FROM concession_sale_line l
                    JOIN concession_sale s ON s.id = l.sale_id
                    JOIN concession_recipe_item ri ON ri.product_id = l.product_id
                    JOIN concession_inventory_item ii ON ii.id = ri.inventory_item_id
                    WHERE s.tenant_id = @tenantId AND s.status = 'paid'
                      AND s.created_at >= @fromUtc AND s.created_at < @toUtc
                    GROUP BY l.product_id
                )
                SELECT COALESCE(rev.name, '(unknown)') AS Name, rev.qty AS QtySold,
                       rev.revenue AS RevenueCents, COALESCE(cogs.cogs, 0)::bigint AS CogsCents
                FROM rev LEFT JOIN cogs ON cogs.pid = rev.pid
                ORDER BY rev.revenue DESC";
            return (await _db.Query<ConcessionItemProfit>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<List<ConcessionCategoryProfit>> GetCategoryProfitability(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                WITH rev AS (
                    SELECT COALESCE(c.name, 'Uncategorized') AS cat,
                           SUM(l.line_total_cents - CASE WHEN s.prices_include_tax THEN l.tax_cents ELSE 0 END)::bigint AS revenue
                    FROM concession_sale_line l
                    JOIN concession_sale s ON s.id = l.sale_id
                    LEFT JOIN concession_product p ON p.id = l.product_id
                    LEFT JOIN concession_category c ON c.id = p.category_id
                    WHERE s.tenant_id = @tenantId AND s.status = 'paid'
                      AND s.created_at >= @fromUtc AND s.created_at < @toUtc
                    GROUP BY COALESCE(c.name, 'Uncategorized')
                ),
                cogs AS (
                    SELECT COALESCE(c.name, 'Uncategorized') AS cat, SUM(l.quantity * ri.quantity * ii.cost_cents)::bigint AS cogs
                    FROM concession_sale_line l
                    JOIN concession_sale s ON s.id = l.sale_id
                    LEFT JOIN concession_product p ON p.id = l.product_id
                    LEFT JOIN concession_category c ON c.id = p.category_id
                    JOIN concession_recipe_item ri ON ri.product_id = l.product_id
                    JOIN concession_inventory_item ii ON ii.id = ri.inventory_item_id
                    WHERE s.tenant_id = @tenantId AND s.status = 'paid'
                      AND s.created_at >= @fromUtc AND s.created_at < @toUtc
                    GROUP BY COALESCE(c.name, 'Uncategorized')
                )
                SELECT rev.cat AS Category, rev.revenue AS RevenueCents, COALESCE(cogs.cogs, 0)::bigint AS CogsCents
                FROM rev LEFT JOIN cogs ON cogs.cat = rev.cat
                ORDER BY rev.revenue DESC";
            return (await _db.Query<ConcessionCategoryProfit>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<List<ConcessionHourRow>> GetHourlyProfitability(Guid tenantId, DateTime fromUtc, DateTime toUtc, string timezone)
        {
            // Bucket net sales by local hour-of-day so the daypart chart reflects the track's timezone.
            const string sql = @"
                SELECT EXTRACT(HOUR FROM (s.created_at AT TIME ZONE @timezone))::int AS Hour,
                       COALESCE(SUM(s.subtotal_cents - s.discount_cents - CASE WHEN s.prices_include_tax THEN s.tax_cents ELSE 0 END), 0)::bigint AS RevenueCents,
                       COUNT(*)::int AS OrderCount
                FROM concession_sale s
                WHERE s.tenant_id = @tenantId AND s.status = 'paid'
                  AND s.created_at >= @fromUtc AND s.created_at < @toUtc
                GROUP BY 1 ORDER BY 1";
            return (await _db.Query<ConcessionHourRow>(sql, new { tenantId, fromUtc, toUtc, timezone })).ToList();
        }

        // Per-employee F&B sales for the staff accountability report. Grouped by the seller; the name is
        // joined from users (sale is already tenant-scoped, so the join is safe). Includes paid sales
        // (totals + tender + tips) and how many of that seller's sales were later refunded.
        public async Task<List<ConcessionEmployeeSalesRow>> GetEmployeeSales(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT s.sold_by_user_id AS UserId,
                       COALESCE(NULLIF(TRIM(COALESCE(u.first_name, '') || ' ' || COALESCE(u.last_name, '')), ''), '') AS Name,
                       COUNT(*) FILTER (WHERE s.status = 'paid')::int AS OrdersCount,
                       COALESCE(SUM(s.total_cents) FILTER (WHERE s.status = 'paid'), 0)::bigint AS GrossSalesCents,
                       COALESCE(SUM(s.subtotal_cents - s.discount_cents - CASE WHEN s.prices_include_tax THEN s.tax_cents ELSE 0 END)
                                FILTER (WHERE s.status = 'paid'), 0)::bigint AS NetSalesCents,
                       COALESCE(SUM(s.tax_cents) FILTER (WHERE s.status = 'paid'), 0)::bigint AS TaxCents,
                       COALESCE(SUM(s.tip_cents) FILTER (WHERE s.status = 'paid'), 0)::bigint AS TipCents,
                       COALESCE(SUM(s.total_cents) FILTER (WHERE s.status = 'paid' AND s.payment_method = 'cash'), 0)::bigint AS CashCents,
                       COALESCE(SUM(s.total_cents) FILTER (WHERE s.status = 'paid' AND s.payment_method IN ('stripe', 'stripe_direct')), 0)::bigint AS CardCents,
                       COUNT(*) FILTER (WHERE s.status = 'refunded')::int AS RefundedCount,
                       COALESCE(SUM(s.total_cents) FILTER (WHERE s.status = 'refunded'), 0)::bigint AS RefundedCents
                FROM concession_sale s
                LEFT JOIN users u ON u.id = s.sold_by_user_id
                WHERE s.tenant_id = @tenantId AND s.status IN ('paid', 'refunded')
                  AND s.created_at >= @fromUtc AND s.created_at < @toUtc
                GROUP BY s.sold_by_user_id, u.first_name, u.last_name
                ORDER BY GrossSalesCents DESC";
            return (await _db.Query<ConcessionEmployeeSalesRow>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }
    }
}
