using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IDayPassPurchaseRepository
    {
        Task<(Guid Id, Guid RedemptionToken)> Create(DayPassPurchase purchase);
        Task<DayPassPurchase?> GetById(Guid id, Guid tenantId);
        Task<DayPassPurchase?> GetByStripePaymentIntentId(string paymentIntentId);
        Task<List<DayPassPurchase>> ListByStripePaymentIntentId(string paymentIntentId);
        Task<DayPassPurchaseWithContext?> GetByRedemptionToken(Guid token, Guid tenantId);
        Task SetStripePaymentIntentId(Guid id, string paymentIntentId);
        Task UpdateStatus(Guid id, string status);
        Task<List<DayPassPurchaseWithContext>> ListForAdmin(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, string? status);
        Task<List<DayPassPurchaseWithContext>> GetForUser(Guid userId, Guid tenantId);
        Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason);
        Task MarkRefunded(Guid id, string? refundNote);
        Task<int> ActiveSpotsReservedForEvent(Guid eventId);
        Task<Dictionary<Guid, int>> ActiveSpotsReservedForEvents(IEnumerable<Guid> eventIds);
        Task<List<DayPassPurchaseWithContext>> ListByStatusAcrossTenants(string status);
    }
}
