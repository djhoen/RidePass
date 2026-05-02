using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IEventTicketPurchaseRepository
    {
        Task<(Guid Id, Guid RedemptionToken)> Create(EventTicketPurchase purchase);
        Task<EventTicketPurchase?> GetById(Guid id, Guid tenantId);
        Task<EventTicketPurchase?> GetByStripePaymentIntentId(string paymentIntentId);
        Task<List<EventTicketPurchase>> ListByStripePaymentIntentId(string paymentIntentId);
        Task<EventTicketPurchaseWithContext?> GetByRedemptionToken(Guid token, Guid tenantId);
        Task SetStripePaymentIntentId(Guid id, string paymentIntentId);
        Task UpdateStatus(Guid id, string status);
        Task<List<EventTicketPurchaseWithContext>> GetForUser(Guid userId, Guid tenantId);
        Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason);
        Task MarkRefunded(Guid id, string? refundNote);
        Task<List<EventTicketPurchaseWithContext>> ListByStatusAcrossTenants(string status);
    }
}
