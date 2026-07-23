namespace Services.Repositories.Data.WaiverData
{
    /// <summary>
    /// One person in the admin Signed Waivers "People" view: signatures collapsed to a
    /// person identity (rider account when present, else name + birthdate for walk-ups).
    /// </summary>
    public class WaiverPersonRow
    {
        public string PersonKey { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string? PersonEmail { get; set; }
        public DateTime? Birthdate { get; set; }
        public bool HasGuardianSignature { get; set; }
        public string? GuardianName { get; set; }
        public string? GuardianPhone { get; set; }
        public DateTime LastSignedAt { get; set; }
        public int SignatureCount { get; set; }
        public bool HasCurrentWaiver { get; set; }
    }
}
