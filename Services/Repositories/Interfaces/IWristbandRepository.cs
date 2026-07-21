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
        /// Resolves a band code back to its entrant. Codes are unique per event but reused across
        /// events, so this matches within the tenant and prefers the current event: only events
        /// that haven't been over for more than a day, newest start first.
        /// </summary>
        Task<WristbandResolution?> ResolveCode(Guid tenantId, string code);

        /// <summary>Band codes for a set of tickets (the gate order view), keyed by ticket id.</summary>
        Task<Dictionary<Guid, string>> GetCodesForTickets(IEnumerable<Guid> ticketIds, Guid tenantId);
    }
}
