using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IEventTicketPurchaseRepository
    {
        /// <summary>New ticket. <paramref name="rider"/> names the rider at sale time (a Gate Sale "another
        /// rider" line, Script0309: their name, birthdate and profile ids); null = no rider columns, as before.</summary>
        Task<(Guid Id, Guid RedemptionToken)> Create(EventTicketPurchase purchase, TicketRiderAtSale? rider = null);
        /// <summary>These tickets of this tenant, in no particular order (one query).</summary>
        Task<List<EventTicketPurchase>> ListByIds(IReadOnlyCollection<Guid> ids, Guid tenantId);
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
        // The ticket and the same rider's other tickets (same registrant), full rows, for the gate.
        Task<List<EventTicketPurchase>> ListRegistrantTickets(Guid ticketId, Guid tenantId);
        // Gate signing link signed: link the new signature to these tickets. Only PAID (never
        // redeemed) tickets of this tenant whose current signature is missing or is not a valid
        // guardian signature; the gate has already decided which ones fail. Returns rows linked.
        // replaceMinorEra: the riders now over 18, whose guardian signature no longer counts (it may be replaced).
        Task<int> LinkWaiverSignature(IReadOnlyCollection<Guid> ticketIds, Guid tenantId, Guid signatureId, Guid waiverId,
            bool replaceMinorEra = false);
        // Operator-app check-in roster: every paid/redeemed attendee for one event with the
        // attributes the app filters on (tier/class, rider vs spectator, checked-in state).
        Task<List<EventRosterRow>> ListEventRoster(Guid eventId, Guid tenantId, DateOnly? onDate = null);
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
        /// <summary>Records a staff ID/age check against this ticket's rider. Tenant-scoped.</summary>
        Task<int> SetIdVerified(Guid id, Guid tenantId, Guid? verifiedByUserId, DateTime? verifiedDob);
        /// <summary>The same check on several tickets of one rider, in one statement; paid / redeemed only.</summary>
        Task<int> SetIdVerifiedMany(IReadOnlyCollection<Guid> ids, Guid tenantId, Guid? verifiedByUserId, DateTime? verifiedDob);
        /// <summary>Undoes a verification recorded in error. Tenant-scoped.</summary>
        Task<int> ClearIdVerified(Guid id, Guid tenantId);
        // Guarded redeem for offline batch sync: flips paid -> redeemed only, returning true
        // if THIS call made the transition (so the first sync wins and duplicates are detected).
        Task<bool> TryMarkRedeemed(Guid id, Guid tenantId, Guid redeemedByUserId, DateTime atUtc);
        Task UndoRedeemed(Guid id, Guid tenantId);

        /// <summary>Records a multi-day ticket's check-in for one local date. False when already
        /// checked in that day. Single-day events use the redeemed status flip instead.</summary>
        Task<bool> TryRecordAttendance(Guid ticketId, Guid tenantId, DateOnly onDate, Guid? byUserId);

        /// <summary>The buyer's rider tickets for this ticket's event that name nobody (no rider name, account or
        /// dependent; live statuses), and whether this ticket is one of them.</summary>
        Task<(int Count, bool Included)> CountUnnamedRiderTickets(EventTicketPurchase ticket, Guid tenantId);

        /// <summary>Local dates this ticket has been checked in on, oldest first.</summary>
        Task<List<DateOnly>> ListAttendanceDates(Guid ticketId, Guid tenantId);
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

        /// <summary>Atomically detach the funding season pass from a DEAD (failed) ticket.
        /// Returns rows affected — exactly one racer wins, so the winner (and only the winner)
        /// hands the ride credit back. Refunds keep the link for audit; this is only for
        /// failed/abandoned payments.</summary>
        Task<int> ClearAppliedSeasonPass(Guid id, Guid tenantId);
        Task<List<EventTicketPurchaseWithContext>> ListByStatusAcrossTenants(string status);

        /// <summary>Tenant-scoped count of purchases in a given status (e.g. cancelled-awaiting-refund
        /// for the dashboard), so callers don't pull every tenant's rows and filter in memory.</summary>
        Task<int> CountByStatusForTenant(Guid tenantId, string status);
    }
}
