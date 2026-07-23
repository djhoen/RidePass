namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>One attendee row on the Compliance Today screen.</summary>
    public class WaiverComplianceItem
    {
        /// <summary>"scan" | "pass" | "rental" | "lesson".</summary>
        public string Source { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public DateTime AtUtc { get; set; }
        /// <summary>"signed" (covered) or "missing" (admit-with-caution).</summary>
        public string WaiverStatus { get; set; } = "missing";
    }
}
