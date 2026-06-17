using Services.Repositories.Data.ExtrasData;

namespace Services.Repositories.Interfaces
{
    public interface IEventExtraRepository
    {
        // ── Products (catalog) ───────────────────────────────────────────────
        Task<List<EventExtraProduct>> ListProducts(Guid tenantId, bool activeOnly);
        Task<EventExtraProduct?> GetProduct(Guid id, Guid tenantId);
        Task<Guid> CreateProduct(EventExtraProduct p);
        Task UpdateProduct(EventExtraProduct p);
        Task DeleteProduct(Guid id, Guid tenantId);

        /// <summary>Atomic bulk update of sort_order for many products at once.</summary>
        Task UpdateProductSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);

        /// <summary>Tenant-wide sold quantity for a product (across all events + variants).</summary>
        Task<int> SumSoldProduct(Guid productId);

        // ── Variants (per-product SKUs) ──────────────────────────────────────
        Task<List<EventExtraVariant>> ListVariants(Guid productId);
        Task<Dictionary<Guid, List<EventExtraVariant>>> ListVariantsForProducts(IEnumerable<Guid> productIds);
        Task<EventExtraVariant?> GetVariant(Guid id);
        Task<Guid> CreateVariant(EventExtraVariant v);
        Task UpdateVariant(EventExtraVariant v);
        Task DeleteVariant(Guid id);

        /// <summary>Tenant-wide sold quantity for a variant (paid + redeemed). Used as the inventory cap.</summary>
        Task<int> SumSoldVariant(Guid variantId);

        /// <summary>Batched variant sold-counts for many variants at once.</summary>
        Task<Dictionary<Guid, int>> SumSoldVariants(IEnumerable<Guid> variantIds);

        // ── Per-event eligibility ────────────────────────────────────────────
        Task<List<EventExtraEligibility>> ListEligibilityForEvent(Guid eventId);
        Task<Dictionary<Guid, List<EventExtraEligibility>>> ListEligibilityForEvents(IEnumerable<Guid> eventIds);
        Task ReplaceEligibility(Guid eventId, IEnumerable<EventExtraEligibility> rows);
        Task<EventExtraEligibility?> GetEligibility(Guid eventId, Guid productId);

        // ── Purchases ────────────────────────────────────────────────────────
        Task<(Guid Id, Guid RedemptionToken)> CreatePurchase(EventExtraPurchase p);
        Task<EventExtraPurchase?> GetPurchase(Guid id);
        Task<EventExtraPurchase?> GetPurchaseByPaymentIntentId(string paymentIntentId);
        Task<List<EventExtraPurchase>> ListByPaymentIntentId(string paymentIntentId);
        Task<EventExtraPurchase?> GetPurchaseByRedemptionToken(Guid token);
        Task SetPaymentIntentId(Guid id, string paymentIntentId);
        Task UpdateStatus(Guid id, string status);
        Task MarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc);

        /// <summary>Tenant-scoped cancel of a paid extra purchase (releases held inventory via status).</summary>
        Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason);
        Task MarkRefunded(Guid id, string? refundNote);

        Task<List<EventExtraPurchase>> ListMine(Guid userId, Guid tenantId);
        Task<List<EventExtraPurchase>> ListForEvent(Guid eventId);

        /// <summary>
        /// Sum of paid quantity for an (event, product) combo. Used for the per-event
        /// inventory cap check at purchase time.
        /// </summary>
        Task<int> SumSold(Guid eventId, Guid productId);

        /// <summary>Batched sold-count for (event, product) across many events.</summary>
        Task<Dictionary<(Guid EventId, Guid ProductId), int>> SumSoldForEvents(IEnumerable<Guid> eventIds);
    }
}
