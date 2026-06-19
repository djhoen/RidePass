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
        // Gate redemption (event+purchaser scope): every ticket a purchaser holds for one
        // event, across orders. Matches by user id when present, else by lower(email).
        Task<List<EventTicketPurchaseWithContext>> ListByEventForPurchaser(
            Guid eventId, Guid tenantId, Guid? purchaserUserId, string? purchaserEmail);
        Task SetStripePaymentIntentId(Guid id, string paymentIntentId);
        Task UpdateStatus(Guid id, string status);
        Task<bool> HasActiveRaceEntry(Guid tenantId, Guid tierId, Guid? purchaserUserId, string? purchaserEmail);
        Task MarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc);
        Task UndoRedeemed(Guid id, Guid tenantId);
        Task SetRaceNumber(Guid id, Guid tenantId, string? raceNumber);
        Task CompleteRegistration(Guid id, Guid tenantId,
            string? riderFirstName, string? riderLastName, DateTime? riderBirthdate, string? bike,
            string? raceNumber, Guid? waiverId, string? waiverSignatureDataUrl, string? parentGuardianName,
            Guid? registrantId);
        // Rider-facing: all of this rider's (non-cancelled) tickets for one event,
        // across any order. Scoped by the rider's user id (Me feed, cross-tenant).
        Task<List<UserEventOrderItem>> ListForUserEvent(Guid userId, Guid eventId);
        // Rider-facing: paid/redeemed rows for one event with tenant + purchaser details,
        // for rebuilding the consolidated order-confirmation email on resend.
        Task<List<OrderConfirmationRow>> ListForOrderConfirmation(Guid userId, Guid eventId);
        Task<List<RegistrationReminderRow>> ListIncompleteForReminder(DateTime cutoffUtc, int take);
        Task MarkRegistrationReminderSent(IEnumerable<Guid> ticketIds);
        Task<List<IncompleteRegistrationTicket>> ListIncompleteForRegistrationByToken(Guid token, Guid tenantId);
        Task<List<EventTicketPurchaseWithContext>> GetForUser(Guid userId, Guid tenantId);
        Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId, string? reason);
        Task MarkRefunded(Guid id, string? refundNote);
        Task<List<EventTicketPurchaseWithContext>> ListByStatusAcrossTenants(string status);
    }
}
