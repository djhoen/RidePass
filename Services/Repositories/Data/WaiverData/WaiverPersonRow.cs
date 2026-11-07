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
        /// <summary>Their latest signature on the current active waiver is a guardian's (signed_by_parent
        /// with a guardian name, the gate's rule).</summary>
        public bool? LatestByGuardian { get; set; }
        public DateTime? LastCurrentSignedAt { get; set; }
        /// <summary>The rider's own account: the one their signatures are on, or the buying account of a
        /// ticket / pass they signed for when that account IS them. Never a signer's / guardian's.</summary>
        public Guid? RiderAccountId { get; set; }
        /// <summary>That account's email (the only address an adult-waiver link may go to).</summary>
        public string? AccountEmail { get; set; }
        /// <summary>18+ on the tenant's today and their latest signature dates from when they were a minor.</summary>
        public bool NeedsAdultWaiver { get; set; }
        /// <summary>An adult-waiver signing link is already open for this rider.</summary>
        public bool AdultLinkOpen { get; set; }
    }
}
