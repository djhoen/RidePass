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
        // Gate lookup by name/email for the rider with no QR. Restricted to events whose check-in
        // window overlaps today (passed as a UTC interval) and to paid/redeemed rows, so it can't be
        // used to browse the tenant's customer list. Matches buyer name, buyer email, or rider name.
        Task<List<GateSearchRow>> SearchForGate(
            Guid tenantId, string query, DateTime todayStartUtc, DateTime todayEndUtc, int limit);
        // Gate check-in waiver panel (event+purchaser scope): the same ticket set as
        // ListByEventForPurchaser, denormalized with tier audience + the linked signature row
        // so the gate can show, per attendee, who signed which waiver and who still owes one.
        Task<List<OrderAttendeeWaiverRow>> ListWaiverStatusForPurchaser(
            Guid eventId, Guid tenantId, Guid? purchaserUserId, string? purchaserEmail);
        // Operator-app check-in roster: every paid/redeemed attendee for one event with the
        // attributes the app filters on (tier/class, rider vs spectator, checked-in state).
        Task<List<EventRosterRow>> ListEventRoster(Guid eventId, Guid tenantId);
        Task SetStripePaymentIntentId(Guid id, string paymentIntentId);
        Task MarkDirectCharge(Guid id, Guid tenantId, string connectedAccountId);
        Task UpdateStatus(Guid id, string status);
        Task<bool> HasActiveRaceEntry(Guid tenantId, Guid tierId, Guid? purchaserUserId, string? purchaserEmail);

        /// <summary>
        /// Per-rider uniqueness within a race class (the set of tiers in classTierIds, i.e. all
        /// price-ladder steps of one class). Returns "person" if the same rider (name + birthdate)
        /// is already entered, "number" if the race number is taken, else null. excludeTicketIds
        /// skip the rows being registered in the same request.
        /// </summary>
        Task<string?> FindRaceClassConflict(Guid tenantId, IReadOnlyList<Guid> classTierIds,
            string firstName, string lastName, DateTime? birthdate, string? raceNumber,
            IReadOnlyList<Guid> excludeTicketIds);
        Task MarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc);
        // Guarded redeem for offline batch sync: flips paid -> redeemed only, returning true
        // if THIS call made the transition (so the first sync wins and duplicates are detected).
        Task<bool> TryMarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc);
        Task UndoRedeemed(Guid id, Guid tenantId);
        Task SetRaceNumber(Guid id, Guid tenantId, string? raceNumber);
        Task<bool> CompleteRegistration(Guid id, Guid tenantId,
            string? riderFirstName, string? riderLastName, DateTime? riderBirthdate, string? bike,
            string? raceNumber, Guid? waiverId, string? waiverSignatureDataUrl, Guid? waiverSignatureId,
            string? parentGuardianName,
            string? emergencyContactName, string? emergencyContactPhone, Guid? registrantId);
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

        /// <summary>Tenant-scoped count of purchases in a given status (e.g. cancelled-awaiting-refund
        /// for the dashboard), so callers don't pull every tenant's rows and filter in memory.</summary>
        Task<int> CountByStatusForTenant(Guid tenantId, string status);
    }
}
