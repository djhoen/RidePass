namespace Services.Repositories.Data.KioskData
{
    /// <summary>A kiosk sign-up as the gate screens list it (staff only).</summary>
    public class KioskSignupRow : KioskSignup
    {
        public string? KioskName { get; set; }
        public string? RiderEmail { get; set; }
        public string? GuardianAccountFirstName { get; set; }
        public string? GuardianAccountLastName { get; set; }
        public string? GuardianAccountEmail { get; set; }
        public DateTime? PhotoTakenAt { get; set; }
        /// <summary>The existing account(s) a pending sign-up matched (shown to staff only).</summary>
        public string? MatchedFirstName { get; set; }
        public string? MatchedLastName { get; set; }
        public string? MatchedGuardianFirstName { get; set; }
        public string? MatchedGuardianLastName { get; set; }
        /// <summary>The birthdate on the matched account, for staff to hold against the photo ID.</summary>
        public DateTime? MatchedBirthdate { get; set; }
        public DateTime? MatchedGuardianBirthdate { get; set; }
    }
}
