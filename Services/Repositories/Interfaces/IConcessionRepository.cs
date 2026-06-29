using Services.Repositories.Data.ConcessionData;

namespace Services.Repositories.Interfaces
{
    public interface IConcessionRepository
    {
        // Products
        Task<List<ConcessionProduct>> ListProducts(Guid tenantId, bool activeOnly);
        Task<ConcessionProduct?> GetProduct(Guid id, Guid tenantId);
        Task<Guid> CreateProduct(ConcessionProduct p);
        Task UpdateProduct(ConcessionProduct p);
        Task DeleteProduct(Guid id, Guid tenantId);
        Task UpdateProductSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);

        // Categories
        Task<List<ConcessionCategory>> ListCategories(Guid tenantId, bool activeOnly);
        Task<Guid> CreateCategory(ConcessionCategory cat);
        Task UpdateCategory(ConcessionCategory cat);
        Task DeleteCategory(Guid id, Guid tenantId);

        // Tax categories
        Task<List<ConcessionTaxCategory>> ListTaxCategories(Guid tenantId);
        Task EnsureDefaultTaxCategory(Guid tenantId);
        Task<Guid> CreateTaxCategory(ConcessionTaxCategory c);
        Task UpdateTaxCategory(ConcessionTaxCategory c);
        Task DeleteTaxCategory(Guid id, Guid tenantId);

        // Menu board settings
        Task<ConcessionMenuSettings?> GetMenuSettings(Guid tenantId);
        Task UpsertMenuSettings(ConcessionMenuSettings s);
        Task MarkStarterSeeded(Guid tenantId);

        // Online-order capacity / throttle
        Task<ConcessionOrderingCapacity?> GetOrderingCapacity(Guid tenantId);
        Task UpsertOrderingCapacity(ConcessionOrderingCapacity c);
        Task SetOnlinePaused(Guid tenantId, bool paused);
        Task<int> CountActiveOrders(Guid tenantId);
        Task<int> CountActivePrepLines(Guid tenantId);

        // Discount presets
        Task<List<ConcessionDiscountPreset>> ListDiscountPresets(Guid tenantId, bool activeOnly);
        Task<ConcessionDiscountPreset?> GetDiscountPreset(Guid id, Guid tenantId);
        Task<Guid> CreateDiscountPreset(ConcessionDiscountPreset p);
        Task UpdateDiscountPreset(ConcessionDiscountPreset p);
        Task DeleteDiscountPreset(Guid id, Guid tenantId);

        // Comp reasons
        Task<List<ConcessionCompReason>> ListCompReasons(Guid tenantId, bool activeOnly);
        Task<ConcessionCompReason?> GetCompReason(Guid id, Guid tenantId);
        Task<Guid> CreateCompReason(ConcessionCompReason c);
        Task UpdateCompReason(ConcessionCompReason c);
        Task DeleteCompReason(Guid id, Guid tenantId);

        // Void/comp report (comped sales in a window)
        Task<List<ConcessionSale>> SearchComps(Guid tenantId, DateTime fromUtc, DateTime toUtc, int take = 500);

        // Inventory items
        Task<List<ConcessionInventoryItem>> ListInventoryItems(Guid tenantId, bool activeOnly);
        Task<ConcessionInventoryItem?> GetInventoryItem(Guid id, Guid tenantId);
        Task<Guid> CreateInventoryItem(ConcessionInventoryItem i);
        Task UpdateInventoryItem(ConcessionInventoryItem i);
        Task DeleteInventoryItem(Guid id, Guid tenantId);
        Task ReceiveStock(Guid id, Guid tenantId, decimal quantity);
        Task<List<ConcessionInventoryItem>> MarkAndGetNewlyLowStock(Guid tenantId);

        // Recipes
        Task<List<ConcessionRecipeLine>> GetRecipe(Guid productId);
        Task SetRecipe(Guid productId, IReadOnlyList<(Guid ItemId, decimal Quantity)> lines);
        Task DepleteInventoryForSale(Guid saleId, Guid tenantId);

        // Combos (shared, tenant-level definition)
        Task<List<ConcessionComboTier>> GetComboTiers(Guid tenantId);
        Task SetComboTiers(Guid tenantId, IReadOnlyList<ConcessionComboTier> tiers);
        Task<List<ConcessionComboSlot>> GetComboSlots(Guid tenantId);
        Task SetComboSlots(Guid tenantId, IReadOnlyList<ConcessionComboSlot> slots);

        // Stock takes
        Task<Guid> CreateInventoryCount(Guid tenantId, Guid? countedBy, string? note, IReadOnlyList<(Guid ItemId, decimal CountedQty)> lines);
        Task<List<(Guid Id, DateTime CreatedAt, string? Note, long VarianceCents)>> ListInventoryCounts(Guid tenantId, int take = 30);
        Task<List<ConcessionInventoryCountLine>> GetInventoryCountLines(Guid countId, Guid tenantId);

        // Variants
        Task<List<ConcessionVariant>> ListVariants(Guid productId);
        Task<Dictionary<Guid, List<ConcessionVariant>>> ListVariantsForProducts(IEnumerable<Guid> productIds);
        Task<ConcessionVariant?> GetVariant(Guid id);
        Task<Guid> CreateVariant(ConcessionVariant v);
        Task UpdateVariant(ConcessionVariant v);
        Task DeleteVariant(Guid id);

        // Sold counts (pending + paid reserve stock) for inventory checks.
        Task<Dictionary<Guid, int>> SumSoldVariants(IEnumerable<Guid> variantIds);
        Task<int> SumSoldVariant(Guid variantId);
        Task<Dictionary<Guid, int>> SumSoldProducts(IEnumerable<Guid> productIds);
        Task<int> SumSoldProduct(Guid productId);
        Task SetProductSoldOut(Guid id, Guid tenantId, DateTime? soldOutDate);

        // Sales
        Task<Guid> CreateSale(ConcessionSale sale);
        Task CreateSaleLines(Guid saleId, IEnumerable<ConcessionSaleLine> lines);
        Task SetSalePaymentIntentId(Guid saleId, string paymentIntentId);
        Task<ConcessionSale?> GetSaleByPaymentIntentId(string paymentIntentId);
        Task<ConcessionSale?> GetSale(Guid id, Guid tenantId);
        Task MarkSalePaid(Guid saleId);
        Task MarkSaleFailed(Guid saleId);
        Task<int> NextOrderNumber(Guid tenantId, DateTime nowUtc);
        Task<List<ConcessionSale>> ListOrdersForPurchaser(Guid tenantId, Guid userId, int take = 20);
        Task SetOrderNumber(Guid saleId, int orderNumber);
        Task MarkSaleRefunded(Guid saleId, Guid tenantId);
        Task<int> FailStalePendingSales(DateTime olderThanUtc);

        // Sale lines + modifiers
        Task<List<ConcessionSaleLine>> GetSaleLines(Guid saleId);

        // Kitchen
        Task<List<ConcessionSale>> GetKitchenSales(Guid tenantId);
        Task<List<ConcessionSaleLine>> GetKitchenLines(Guid tenantId, Guid? stationId);
        Task<bool> AdvanceLinePrep(Guid lineId, Guid tenantId, string prepStatus);
        Task RecomputeSaleFulfillment(Guid saleId, Guid tenantId);
        Task<(int Count, double AvgPrepSeconds)> GetKitchenStats(Guid tenantId, DateTime sinceUtc);
        Task<bool> TryMarkReadyNotified(Guid saleId, Guid tenantId);
        Task MarkSaleCompleted(Guid saleId, Guid tenantId);
        Task RecallSale(Guid saleId, Guid tenantId);
        Task<List<ConcessionSale>> ListRecentlyCompleted(Guid tenantId, int take = 15);
        Task<List<ConcessionSale>> SearchSales(Guid tenantId, string? query, DateTime? fromUtc, DateTime? toUtc, int take = 200);
        Task SetRush(Guid saleId, Guid tenantId, bool isRush);

        // Stations
        Task<List<ConcessionStation>> ListStations(Guid tenantId, bool activeOnly);
        Task<Guid> CreateStation(ConcessionStation s);
        Task UpdateStation(ConcessionStation s);
        Task DeleteStation(Guid id, Guid tenantId);

        // Modifier groups + options
        Task<List<ConcessionModifierGroup>> ListModifierGroups(Guid tenantId, bool activeOnly);
        Task SeedStarterCatalog(Guid tenantId, bool onlyIfEmpty);
        Task<ConcessionModifierGroup?> GetModifierGroup(Guid id, Guid tenantId);
        Task<Guid> CreateModifierGroup(ConcessionModifierGroup g);
        Task UpdateModifierGroup(ConcessionModifierGroup g);
        Task DeleteModifierGroup(Guid id, Guid tenantId);
        Task<List<ConcessionModifierOption>> ListOptionsForGroups(IEnumerable<Guid> groupIds, bool activeOnly);
        Task<ConcessionModifierOption?> GetOption(Guid id);
        Task<Dictionary<Guid, string>> GetOptionNames(IEnumerable<Guid> optionIds);
        Task<Guid> CreateOption(ConcessionModifierOption o);
        Task UpdateOption(ConcessionModifierOption o);
        Task DeleteOption(Guid id);

        // Product -> modifier group assignment
        Task<List<Guid>> GetProductGroupIds(Guid productId);
        Task SetProductGroups(Guid productId, IReadOnlyList<Guid> groupIds);
        Task<Dictionary<Guid, List<Guid>>> ListProductGroupLinks(IEnumerable<Guid> productIds);

        // Product default modifier options (pre-selected on add)
        Task<List<Guid>> GetProductDefaultOptionIds(Guid productId);
        Task SetProductDefaultOptions(Guid productId, IReadOnlyList<Guid> optionIds);
        Task<Dictionary<Guid, List<Guid>>> ListProductDefaultOptionLinks(IEnumerable<Guid> productIds);

        // ── Profitability reporting (paid sales in a date range) ──────────────────
        Task<ConcessionSalesAggregate> GetSalesAggregate(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<long> GetCogsTotal(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<ConcessionRefundAggregate> GetRefundAggregate(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<List<ConcessionPaymentRow>> GetPaymentBreakdown(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<List<ConcessionItemProfit>> GetItemProfitability(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<List<ConcessionCategoryProfit>> GetCategoryProfitability(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<List<ConcessionHourRow>> GetHourlyProfitability(Guid tenantId, DateTime fromUtc, DateTime toUtc, string timezone);
        Task<List<ConcessionEmployeeSalesRow>> GetEmployeeSales(Guid tenantId, DateTime fromUtc, DateTime toUtc);
    }
}
