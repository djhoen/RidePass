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
        /// The band in this store live on that tenant-local date with that code (ticket or pass admission alike),
        /// newest first: an event band from its event's start through its end plus one day, a walk-up band on its
        /// own date. The one window used both to resolve a scan and to ask whether a code is in use that day.
        /// </summary>
        Task<WristbandResolution?> ResolveCodeOn(Guid tenantId, string code, DateOnly date, string timezone);

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
