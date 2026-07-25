using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IWristbandRepository
    {
        /// <summary>
        /// Links a band code to a ticket, replacing any band the ticket already wears. Returns
        /// null on success, or the OTHER ticket's holder description when the code is already
        /// linked to someone else at this event (staff need to know whose wrist it's on).
        /// Linking the same code to the same ticket again is an idempotent success.
        /// </summary>
        Task<string?> Link(Guid tenantId, Guid eventId, Guid ticketId, string code, Guid? byUserId);

        /// <summary>Removes a ticket's band link. Returns rows affected.</summary>
        Task<int> UnlinkTicket(Guid ticketId, Guid tenantId);

        /// <summary>
        /// Resolves a band code back to its wearer, ticket or season pass admission alike. Codes are
        /// unique per scope but reused across scopes, so this matches within the tenant and prefers
        /// what is live: events not over for more than a day, or a walk-up band issued for
        /// <paramref name="todayLocal"/>, newest first.
        /// </summary>
        Task<WristbandResolution?> ResolveCode(Guid tenantId, string code, DateOnly todayLocal);

        /// <summary>Band codes for a set of tickets (the gate order view), keyed by ticket id.</summary>
        Task<Dictionary<Guid, string>> GetCodesForTickets(IEnumerable<Guid> ticketIds, Guid tenantId);

        /// <summary>
        /// Links a band code to a season pass admission (a checked_in reservation), replacing any
        /// band that admission already wears. Returns null on success, or the OTHER holder's name
        /// when the code is already linked to someone else in the same scope: the reservation's
        /// event when it has one, otherwise the tenant-local date. Re-linking the same code to the
        /// same admission is an idempotent success.
        /// </summary>
        Task<string?> LinkToReservation(Guid tenantId, Guid reservationId, Guid? eventId, DateOnly? validOnDate,
            string code, Guid? byUserId);

        /// <summary>Removes a season pass admission's band link. Returns rows affected.</summary>
        Task<int> UnlinkReservation(Guid reservationId, Guid tenantId);

        /// <summary>Band codes for a set of season pass admissions, keyed by reservation id.</summary>
        Task<Dictionary<Guid, string>> GetCodesForReservations(IEnumerable<Guid> reservationIds, Guid tenantId);
    }
}
