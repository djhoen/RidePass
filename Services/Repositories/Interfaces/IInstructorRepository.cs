using Services.Repositories.Data.InstructorData;

namespace Services.Repositories.Interfaces
{
    public interface IInstructorRepository
    {
        // ── Instructor CRUD ──────────────────────────────────────────────────
        Task<List<Instructor>> List(Guid tenantId, bool activeOnly);
        Task<Instructor?> Get(Guid id, Guid tenantId);
        Task<Guid> Create(Instructor i);
        Task Update(Instructor i);
        Task Delete(Guid id, Guid tenantId);

        // ── Event assignment ─────────────────────────────────────────────────
        /// <summary>Instructors currently assigned to an event, ordered by sort_order/name.</summary>
        Task<List<Instructor>> ListForEvent(Guid eventId, Guid tenantId);

        /// <summary>Assigned-instructor ids for many events at once (event_id → [instructor_id]).</summary>
        Task<Dictionary<Guid, List<Guid>>> ListAssignmentsForEvents(IEnumerable<Guid> eventIds);

        /// <summary>Replace the full instructor set assigned to an event. Empty list clears it.</summary>
        Task ReplaceEventInstructors(Guid eventId, IEnumerable<Guid> instructorIds);

        /// <summary>
        /// For the given instructor ids, return any OTHER scheduled event whose time window
        /// overlaps [startsAt, endsAt) (half-open). Used to block double-booking a coach.
        /// excludeEventId skips the event being edited so it doesn't clash with itself.
        /// </summary>
        Task<List<InstructorConflict>> FindConflicts(
            Guid tenantId, IReadOnlyList<Guid> instructorIds,
            DateTime startsAt, DateTime endsAt, Guid? excludeEventId);
    }
}
