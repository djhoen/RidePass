using Services.Repositories.Data.RentalData;

namespace Services.Repositories.Interfaces
{
    public interface IRentalRepository
    {
        // ── Products ─────────────────────────────────────────────────────────
        Task<List<RentalProduct>> ListProducts(Guid tenantId, bool activeOnly);
        Task<RentalProduct?> GetProduct(Guid id, Guid tenantId);
        Task<Guid> CreateProduct(RentalProduct p);
        Task UpdateProduct(RentalProduct p);
        Task DeleteProduct(Guid id, Guid tenantId);

        /// <summary>Atomic bulk update of sort_order for many rental products at once.</summary>
        Task UpdateProductSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);

        // ── Per-item units ───────────────────────────────────────────────────
        Task<List<RentalItem>> ListItems(Guid productId);
        Task<RentalItem?> GetItem(Guid id, Guid tenantId);
        Task<Guid> CreateItem(RentalItem item);
        Task UpdateItem(RentalItem item);
        Task DeleteItem(Guid id, Guid tenantId);

        /// <summary>
        /// Returns the count of available (not maintenance/retired) per-item units that
        /// have NO overlapping reservation in [from, to]. Used for capacity check on
        /// per_item products.
        /// </summary>
        Task<int> CountAvailablePerItemUnits(Guid productId, DateTime fromDate, DateTime toDate);

        /// <summary>
        /// Returns up to <paramref name="quantity"/> per-item ids that are available
        /// across [from, to]. Used at booking time to assign specific units to a
        /// per_item rental purchase.
        /// </summary>
        Task<List<Guid>> PickAvailablePerItemUnits(Guid productId, DateTime fromDate, DateTime toDate, int quantity);

        /// <summary>
        /// Sum of quantity across reservation-holding rentals (paid/out) for a pool product
        /// whose date window overlaps [from, to]. Capacity check = inventory_pool - this.
        /// </summary>
        Task<int> SumOverlappingPoolReserved(Guid productId, DateTime fromDate, DateTime toDate);

        // ── Purchases ────────────────────────────────────────────────────────
        Task<(Guid Id, Guid RedemptionToken)> CreatePurchase(RentalPurchase p);
        Task<RentalPurchase?> GetPurchase(Guid id);
        Task<RentalPurchase?> GetPurchaseByRedemptionToken(Guid token);
        Task<RentalPurchase?> GetPurchaseByRentalPaymentIntentId(string paymentIntentId);
        Task<RentalPurchase?> GetPurchaseByDepositPaymentIntentId(string paymentIntentId);

        Task SetRentalPaymentIntentId(Guid id, string paymentIntentId);
        Task SetDepositPaymentIntentId(Guid id, string paymentIntentId);
        Task UpdateStatus(Guid id, string status);

        Task MarkOut(Guid id, DateTime atUtc);
        Task MarkReturned(Guid id, DateTime atUtc, string? conditionNotes, int depositCapturedCents, bool damaged);

        Task AssignItems(Guid purchaseId, IEnumerable<Guid> itemIds);
        Task<List<RentalPurchaseItem>> ListAssignedItems(Guid purchaseId);

        /// <summary>Stamp the checkout photo + notes onto a per-item assignment row.</summary>
        Task SetCheckoutCondition(Guid purchaseItemId, string? photoDataUrl, string? notes);

        /// <summary>Stamp the return photo + notes onto a per-item assignment row.</summary>
        Task SetReturnCondition(Guid purchaseItemId, string? photoDataUrl, string? notes);

        Task<List<RentalPurchase>> ListMine(Guid userId, Guid tenantId);
        Task<List<RentalPurchase>> ListForCounter(Guid tenantId, DateTime fromUtc, DateTime toUtc, string? status);

        // ── Maintenance windows ──────────────────────────────────────────────
        Task<List<RentalItemMaintenance>> ListMaintenanceForItem(Guid itemId);
        Task<List<RentalItemMaintenance>> ListUpcomingMaintenanceForProduct(Guid productId);
        Task<RentalItemMaintenance?> GetMaintenance(Guid id, Guid tenantId);
        Task<Guid> AddMaintenance(RentalItemMaintenance m);
        Task UpdateMaintenance(RentalItemMaintenance m);
        Task DeleteMaintenance(Guid id, Guid tenantId);
    }
}
