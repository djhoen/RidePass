using Services.Repositories.Data.LessonData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Lesson date changes and weather cancellations (Script0319). Every call is tenant-scoped.</summary>
    public interface ILessonChangeRepository
    {
        Task<LessonTicketContext?> GetTicket(Guid tenantId, Guid ticketId);

        /// <summary>Which of these tickets are lesson bookings, and whether their session was weather-cancelled.</summary>
        Task<List<LessonTicketFlag>> LessonFlags(Guid tenantId, IReadOnlyList<Guid> ticketIds);

        /// <summary>
        /// Groups in other scheduled sessions of the same event type that start after
        /// <paramref name="notBeforeUtc"/> and have spots left (group inventory, coach cap, event capacity).
        /// The caller narrows them to the same group name.
        /// </summary>
        Task<List<LessonRebookOption>> ListOptions(Guid tenantId, Guid eventTypeId, Guid excludeEventId,
            string tierKind, DateTime notBeforeUtc);

        /// <summary>True when the buyer rented a bike for this session (moving the lesson would strand it).</summary>
        Task<bool> HasSessionRental(Guid tenantId, Guid eventId, Guid? purchaserUserId, string purchaserEmail);

        /// <summary>
        /// Moves a paid ticket to <paramref name="toTierId"/> if that group (and its event) still has room.
        /// Returns null on success, else why not. Callers hold the target event's capacity lock.
        /// </summary>
        Task<string?> MoveTicket(Guid tenantId, Guid ticketId, Guid fromTierId, Guid toTierId, string source, Guid? byUserId);

        /// <summary>Sessions of lesson-type events overlapping the range.</summary>
        Task<List<LessonSessionRow>> ListSessions(Guid tenantId, DateTime fromUtc, DateTime toUtc);

        Task<List<LessonBookingRow>> ListBookings(Guid tenantId, Guid eventId);

        /// <summary>Bikes rented for the session, with the rider size each was set up for.</summary>
        Task<List<LessonBikeRow>> ListBikes(Guid tenantId, Guid eventId);

        /// <summary>Cancels the event for weather. False when it isn't this venue's or was already weather-cancelled.</summary>
        Task<bool> WeatherCancel(Guid tenantId, Guid eventId, Guid byUserId);
    }
}
