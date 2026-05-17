using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IPassPurchaseRepository
    {
        Task<(Guid Id, Guid RedemptionToken)> Create(PassPurchase purchase);
        Task<PassPurchase?> GetById(Guid id, Guid tenantId);
        Task<PassPurchase?> GetByStripePaymentIntentId(string paymentIntentId);
        Task<List<PassPurchase>> ListByStripePaymentIntentId(string paymentIntentId);
        Task<PassPurchaseWithContext?> GetByRedemptionToken(Guid token, Guid tenantId);
        Task SetStripePaymentIntentId(Guid id, string paymentIntentId);
        Task UpdateStatus(Guid id, string status);
        Task MarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc);
        Task UndoRedeemed(Guid id, Guid tenantId);
        Task<List<PassPurchaseWithContext>> ListForAdmin(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, string? status);
        Task<List<PassPurchaseWithContext>> GetForUser(Guid userId, Guid tenantId);
        Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason);
        Task MarkRefunded(Guid id, string? refundNote);
        Task<int> ActiveSpotsReservedForEvent(Guid eventId);
        Task<Dictionary<Guid, int>> ActiveSpotsReservedForEvents(IEnumerable<Guid> eventIds);
        Task<List<PassPurchaseWithContext>> ListByStatusAcrossTenants(string status);
    }
}
