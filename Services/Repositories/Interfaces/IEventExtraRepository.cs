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
        Task MarkDirectCharge(Guid id, Guid tenantId, string connectedAccountId);
        Task UpdateStatus(Guid id, string status);
        /// <summary>
        /// Check in an add-on. The 'paid' guard lives in the SQL, so this returns false when the row
        /// was cancelled, refunded, or already checked in, rather than silently doing nothing.
        /// </summary>
        Task<bool> MarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc);

        /// <summary>
        /// Reverse a check-in. Guarded on 'redeemed' so it can only ever undo an actual check-in,
        /// never resurrect a cancelled or refunded add-on into a usable one. Returns false when
        /// nothing matched, which the caller reports rather than claiming success.
        /// </summary>
        Task<bool> UndoRedeemed(Guid id, Guid tenantId);

        /// <summary>
        /// The add-on check-in list: who bought a given add-on in a window, and who has arrived.
        /// Exists because the gate's scan flow can only reach an add-on through a QR, which is no
        /// use to whoever is working a campground with a clipboard, and because a customer who
        /// bought ONLY an add-on has no ticket for the gate search to find them by.
        ///
        /// All filters are optional and AND together. <paramref name="query"/> matches the
        /// purchaser's name or email. The window is on the EVENT date where there is one and the
        /// purchase date otherwise, since a no-event add-on has no other date to sort by.
        /// </summary>
        Task<List<ExtraCheckInRow>> SearchForCheckIn(
            Guid tenantId, Guid? productId, string? kind, Guid? eventId,
            DateTime? fromUtc, DateTime? toUtc, string? query, bool arrivedOnly, bool notArrivedOnly,
            int limit);

        /// <summary>Tenant-scoped cancel of a paid extra purchase (releases held inventory via status).</summary>
        Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason);
        Task MarkRefunded(Guid id, string? refundNote);

        Task<List<EventExtraPurchase>> ListMine(Guid userId, Guid tenantId);
        Task<List<EventExtraPurchase>> ListForEvent(Guid eventId);
        // Gate redemption (event+purchaser scope): a purchaser's add-ons for one event,
        // across orders. Matches by user id when present, else by lower(email). Carries the
        // catalog product name so the gate can show what was actually bought.
        Task<List<EventExtraPurchaseWithProduct>> ListByEventForPurchaser(
            Guid eventId, Guid tenantId, Guid? purchaserUserId, string? purchaserEmail);

        /// <summary>Single add-on purchase joined to its catalog product (tenant-scoped), for the
        /// no-event counter-merch path at the gate where only the scanned row is in scope.</summary>
        Task<EventExtraPurchaseWithProduct?> GetPurchaseWithProduct(Guid id, Guid tenantId);

        /// <summary>
        /// Sum of paid quantity for an (event, product) combo. Used for the per-event
        /// inventory cap check at purchase time.
        /// </summary>
        Task<int> SumSold(Guid eventId, Guid productId);

        /// <summary>Batched sold-count for (event, product) across many events.</summary>
        Task<Dictionary<(Guid EventId, Guid ProductId), int>> SumSoldForEvents(IEnumerable<Guid> eventIds);
    }
}
