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

        // Menu boards (one per in-venue screen)
        Task<List<ConcessionMenuBoard>> ListMenuBoards(Guid tenantId, bool activeOnly);
        Task<Guid> CreateMenuBoard(ConcessionMenuBoard b);
        Task UpdateMenuBoard(ConcessionMenuBoard b);
        Task DeleteMenuBoard(Guid id, Guid tenantId);

        // Menu board promo tiles
        Task<List<ConcessionMenuPromo>> ListMenuPromos(Guid tenantId, bool activeOnly);
        Task<Guid> CreateMenuPromo(ConcessionMenuPromo p);
        Task UpdateMenuPromo(ConcessionMenuPromo p);
        Task DeleteMenuPromo(Guid id, Guid tenantId);

        // Customer-facing POS display sessions
        Task<Guid> CreateDisplay(Guid tenantId, string pairCode);
        Task<ConcessionDisplay?> GetDisplay(Guid id, Guid tenantId);
        Task<ConcessionDisplay?> GetDisplayByCode(string pairCode, Guid tenantId);
        Task UpdateDisplayState(Guid id, Guid tenantId, string? stateJson);
        Task SetDisplayTip(Guid id, Guid tenantId, int tipCents);

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
        /// <summary>Depletes recipe inventory for a sale's lines; <paramref name="roundNumber"/> limits it
        /// to one tab round (tabs deplete per round as each fires, never the whole tab at close).</summary>
        Task DepleteInventoryForSale(Guid saleId, Guid tenantId, int? roundNumber = null);

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
        /// <summary>pending/open -> paid. When <paramref name="paymentIntentId"/> is given the flip is pinned to the
        /// row still pointing at that intent, so a finalize for an intent the sale has since moved away from matches
        /// nothing. Returns whether THIS call flipped the row (false = already paid, or no longer waiting on it).</summary>
        Task<bool> MarkSalePaid(Guid saleId, string? paymentIntentId = null);
        /// <summary>Dead-payment flip from 'pending'. status is 'failed' (Stripe reported a
        /// declined attempt) or 'abandoned' (no attempt ever completed; reconciler only).</summary>
        Task MarkSaleFailed(Guid saleId, string status = "failed");
        Task<int> NextOrderNumber(Guid tenantId);
        Task<List<ConcessionSale>> ListOrdersForPurchaser(Guid tenantId, Guid userId, int take = 20);
        Task SetOrderNumber(Guid saleId, int orderNumber);
        Task MarkSaleRefunded(Guid saleId, Guid tenantId);
        /// <summary>Blind age sweep of pending sales; status as on <see cref="MarkSaleFailed"/>.
        /// The reconciler's walk-off sweep passes 'abandoned'.</summary>
        Task<List<StalePendingSale>> FailStalePendingSales(DateTime olderThanUtc, string status = "failed");

        // Sale lines + modifiers
        Task<List<ConcessionSaleLine>> GetSaleLines(Guid saleId);

        // Kitchen
        Task<List<ConcessionSale>> GetKitchenSales(Guid tenantId);
        Task<List<ConcessionSaleLine>> GetKitchenLines(Guid tenantId, Guid? stationId);
        Task<bool> AdvanceLinePrep(Guid lineId, Guid tenantId, string prepStatus);
        /// <summary>Bumps every not-yet-ready line on one order to 'ready' (or just one tab round when
        /// <paramref name="roundNumber"/> is given). Returns rows changed.</summary>
        Task<int> MarkAllLinesReady(Guid saleId, Guid tenantId, int? roundNumber = null);
        Task RecomputeSaleFulfillment(Guid saleId, Guid tenantId);
        Task<(int Count, double AvgPrepSeconds)> GetKitchenStats(Guid tenantId, DateTime sinceUtc);
        Task<bool> TryMarkReadyNotified(Guid saleId, Guid tenantId);
        Task MarkSaleCompleted(Guid saleId, Guid tenantId);
        Task RecallSale(Guid saleId, Guid tenantId);
        Task<List<ConcessionSale>> ListRecentlyCompleted(Guid tenantId, int take = 15);
        Task<List<ConcessionSale>> SearchSales(Guid tenantId, string? query, DateTime? fromUtc, DateTime? toUtc, int take = 200);
        Task SetRush(Guid saleId, Guid tenantId, bool isRush);

        // Open tabs (Script0291). A tab is a concession_sale in status 'open'. Every method here is
        // tenant-scoped, and every write is a CONDITIONAL update whose WHERE pins the exact prior state
        // it expects (status 'open', and which PaymentIntent, if any, the row points at) and reports
        // whether it matched. ConcessionController serializes all mutations of one tab under a per-sale
        // advisory lock; these predicates are the backstop that makes a stale caller (a register that
        // read the tab before another one closed it, a late webhook) a no-op instead of a double close.
        Task<List<ConcessionOpenTabRow>> ListOpenTabs(Guid tenantId);
        /// <summary>Count + running total of the tenant's open tabs (End of Day "still open").</summary>
        Task<(int Count, long TotalCents)> GetOpenTabsAggregate(Guid tenantId);
        /// <summary>Rewrites the sale-level money/discount/credit/purchaser snapshot of an OPEN tab from the entity.
        /// Pinned to the row still pointing at the entity's StripePaymentIntentId (normally none). False when it didn't match.</summary>
        Task<bool> UpdateTabTotals(ConcessionSale sale);
        /// <summary>
        /// Appends one round in a single transaction: locks the sale row, refuses unless it is open with no
        /// card close in flight, numbers the round one past the highest already stored (computed INSIDE the
        /// transaction, so two registers can never share a round), inserts the lines, and persists the
        /// entity's recomputed totals. Returns the round number, or null (nothing written) when the tab
        /// was no longer appendable.
        /// </summary>
        Task<int?> AppendTabRound(ConcessionSale sale, IReadOnlyList<ConcessionSaleLine> lines);
        /// <summary>The tab whose card HOLD is this PaymentIntent (not its settling PI), so hold-lifecycle webhooks can be recognized.</summary>
        Task<ConcessionSale?> GetSaleByHoldPaymentIntentId(string paymentIntentId);
        Task<bool> RenameTab(Guid saleId, Guid tenantId, string? tabName, string? customerName);
        /// <summary>Records (or clears, with nulls) the card hold on an OPEN tab. False when the tab was no longer open.</summary>
        Task<bool> SetTabHold(Guid saleId, Guid tenantId, string? holdPaymentIntentId, int? holdAmountCents);
        /// <summary>Stores the held card's brand/last4/cardholder on an open tab still holding <paramref name="holdPaymentIntentId"/>;
        /// fills tab_name with <paramref name="autoName"/> only when it is still blank. False when it didn't match.</summary>
        Task<bool> SetTabHoldCard(Guid saleId, Guid tenantId, string holdPaymentIntentId, string? brand, string? last4, string? cardholderName, string? autoName);
        /// <summary>Points an open tab at the PaymentIntent that will settle it (a new card-present PI, or the hold being
        /// captured) and the tender it records as. Matches only a tab with no other intent in flight (re-pointing at the
        /// same intent is allowed: re-arming a hold whose first capture attempt had an unknown outcome). False when it didn't match.</summary>
        Task<bool> SetTabCardPayment(Guid saleId, Guid tenantId, string paymentIntentId, string paymentMethod, string? connectedAccountId);
        /// <summary>Takes a cancelled PaymentIntent off an open tab. Pinned to the row still pointing at exactly that intent.</summary>
        Task<bool> ClearTabCardPayment(Guid saleId, Guid tenantId, string paymentIntentId);
        /// <summary>open -> paid right now (cash, or nothing due). Matches only a tab with NO card close in flight. False when it didn't match.</summary>
        Task<bool> CloseTabPaid(Guid saleId, Guid tenantId, string paymentMethod);
        /// <summary>open -> voided: stamps the reason/manager, completes fulfillment and serves every outstanding line.
        /// Matches only a tab with NO card close in flight (the caller cancels it first). False when it didn't match.</summary>
        Task<bool> VoidTab(Guid saleId, Guid tenantId, string reason, string? voidedByName);
        /// <summary>Kitchen "picked up" for one tab round: sets served_at on its lines. Returns rows changed.</summary>
        Task<int> MarkRoundServed(Guid saleId, Guid tenantId, int roundNumber);
        /// <summary>A paid tab whose every line has been served is done: completes it so it leaves the board and pickup screen.</summary>
        Task CompleteTabIfAllServed(Guid saleId, Guid tenantId);

        // Stations
        Task<List<ConcessionStation>> ListStations(Guid tenantId, bool activeOnly);
        Task<Guid> CreateStation(ConcessionStation s);
        Task UpdateStation(ConcessionStation s);
        Task DeleteStation(Guid id, Guid tenantId);

        // Kitchen ticket printers. StationIds on each result is the printer's scope; empty = whole order.
        Task<List<ConcessionPrinter>> ListPrinters(Guid tenantId, bool activeOnly);
        Task<Guid> CreatePrinter(ConcessionPrinter p);
        Task UpdatePrinter(ConcessionPrinter p);
        Task DeletePrinter(Guid id, Guid tenantId);

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
