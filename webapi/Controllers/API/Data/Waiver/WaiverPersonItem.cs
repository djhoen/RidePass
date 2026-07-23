namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>One person in the admin Signed Waivers "People" view.</summary>
    public class WaiverPersonItem
    {
        public string PersonKey { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string? PersonEmail { get; set; }
        public DateTime? Birthdate { get; set; }
        public bool IsMinor { get; set; }
        /// <summary>Turns 18 within the next 90 days, so a guardian-signed waiver is about to lapse.</summary>
        public bool AgingOutSoon { get; set; }
        public bool HasGuardianSignature { get; set; }
        public string? GuardianName { get; set; }
        public string? GuardianPhone { get; set; }
        public DateTime LastSignedAtUtc { get; set; }
        public int SignatureCount { get; set; }
        public bool HasCurrentWaiver { get; set; }
    }
}
