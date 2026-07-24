namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>One expected-today attendee row on the Compliance Today screen.</summary>
    public class WaiverComplianceItem
    {
        /// <summary>"ticket" | "pass" | "rental" | "lesson".</summary>
        public string Source { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string? Email { get; set; }
        /// <summary>Sort anchor (check-in when it happened, else event start).</summary>
        public DateTime AtUtc { get; set; }
        /// <summary>When they signed their current waiver. Null = nothing on file.</summary>
        public DateTime? SignedAtUtc { get; set; }
        /// <summary>When they came through the gate today. Null = not checked in yet.</summary>
        public DateTime? CheckedInAtUtc { get; set; }
        /// <summary>"signed" (covered) or "missing" (admit-with-caution).</summary>
        public string WaiverStatus { get; set; } = "missing";
    }
}
