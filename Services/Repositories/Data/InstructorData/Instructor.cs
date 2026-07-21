namespace Services.Repositories.Data.InstructorData
{
    /// <summary>
    /// A per-tenant coach/instructor who can be assigned to lesson events. An
    /// instructor may never hold two overlapping lessons; that check runs in the
    /// API against event_instructor at assignment time.
    /// </summary>
    public class Instructor
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Bio { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 100;
        /// <summary>How many students this coach can take in one session. Caps a training
        /// group alongside the tier's own inventory: effective cap = min(the two).</summary>
        public int MaxStudentsPerSession { get; set; } = 8;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// A scheduling clash: an instructor already assigned to another event whose
    /// time window overlaps the one being scheduled. Surfaced to the admin so they
    /// can't double-book a coach.
    /// </summary>
    public class InstructorConflict
    {
        public Guid InstructorId { get; set; }
        public string InstructorName { get; set; } = null!;
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
    }
}
