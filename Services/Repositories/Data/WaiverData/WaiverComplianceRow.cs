namespace Services.Repositories.Data.WaiverData
{
    /// <summary>One attendee on the admin Compliance Today screen.</summary>
    public class WaiverComplianceRow
    {
        /// <summary>"scan" | "pass" | "rental" | "lesson".</summary>
        public string Source { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public Guid? UserId { get; set; }
        public DateTime At { get; set; }
        /// <summary>The purchase/rental itself carries a signature (per-context signing).</summary>
        public bool SignedForThis { get; set; }
        /// <summary>The person has a signature on a currently-active waiver (account level).</summary>
        public bool HasCurrentWaiver { get; set; }
    }
}
