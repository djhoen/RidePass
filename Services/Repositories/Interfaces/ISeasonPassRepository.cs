using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface ISeasonPassRepository
    {
        // Products
        Task<List<SeasonPassProduct>> ListProductsForTenant(Guid tenantId, bool activeOnly);
        Task<SeasonPassProduct?> GetProduct(Guid id, Guid tenantId);
        Task<Guid> CreateProduct(SeasonPassProduct p);
        Task UpdateProduct(SeasonPassProduct p);
        Task DeleteProduct(Guid id, Guid tenantId);

        /// <summary>Atomic bulk update of sort_order for many season pass products at once.</summary>
        Task UpdateProductSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);

        // Perks
        Task<List<SeasonPassEventTypePerk>> ListPerks(Guid passProductId);
        Task ReplacePerks(Guid passProductId, IEnumerable<SeasonPassEventTypePerk> perks);

        // Purchases
        Task<(Guid Id, Guid RedemptionToken)> CreatePurchase(SeasonPassPurchase p);
        Task<SeasonPassPurchase?> GetPurchase(Guid id);
        Task<SeasonPassPurchase?> GetPurchaseByStripePaymentIntentId(string paymentIntentId);
        Task<SeasonPassPurchase?> GetPurchaseByRedemptionToken(Guid token);
        Task<List<SeasonPassPurchaseWithContext>> ListMine(Guid userId, Guid tenantId);
        Task SetPurchaseStripePaymentIntentId(Guid id, string paymentIntentId);
        Task UpdatePurchaseStatus(Guid id, string status);
        Task DecrementCredits(Guid purchaseId);

        // Reservations
        Task<Guid> CreateReservation(SeasonPassReservation r);
        Task<SeasonPassReservation?> GetReservation(Guid purchaseId, Guid eventId);
        Task<List<SeasonPassReservationWithContext>> ListReservationsForPurchase(Guid purchaseId);
        Task<List<SeasonPassReservationWithContext>> ListReservationsForPurchaseOnDate(Guid purchaseId, DateTime atUtc, DateTime untilUtc);
        /// <summary>
        /// Updates a reservation's status. <paramref name="tenantId"/> is required and the
        /// SQL filter joins through season_pass_purchase to refuse updates against
        /// reservations that belong to a different tenant — so a staff JWT scoped to
        /// tenant A can't flip reservation status on tenant B's records.
        /// </summary>
        Task UpdateReservationStatus(Guid id, Guid tenantId, string status, Guid? checkedInByUserId = null);

        // Capacity helper — number of active (reserved + checked_in) season pass spots per event.
        Task<Dictionary<Guid, int>> ActiveReservationsForEvents(IEnumerable<Guid> eventIds);
    }
}
