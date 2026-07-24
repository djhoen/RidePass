namespace Services.Repositories.Data.WaiverData
{
    /// <summary>One attendee on the admin Compliance Today screen.</summary>
    public class WaiverComplianceRow
    {
        /// <summary>"ticket" | "pass" | "rental" | "lesson".</summary>
        public string Source { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public Guid? UserId { get; set; }
        /// <summary>Sort anchor: check-in moment when it happened, else the event start.</summary>
        public DateTime At { get; set; }
        /// <summary>When the person actually came through (scan / pass check-in / rental
        /// checkout). Null = expected today but not here yet.</summary>
        public DateTime? CheckedInAt { get; set; }
        /// <summary>The purchase/rental itself carries a signature (per-context signing).</summary>
        public bool SignedForThis { get; set; }
        /// <summary>The signature's own signed_at when the row links one, else the person's
        /// newest signature on a currently-active waiver. Null = no waiver on file.</summary>
        public DateTime? OwnSignedAt { get; set; }
        public DateTime? SignedAt { get; set; }
    }
}
