namespace Services.Repositories.Data.KioskData
{
    /// <summary>A kiosk sign-up as the gate screens list it (staff only).</summary>
    public class KioskSignupRow : KioskSignup
    {
        public string? KioskName { get; set; }
        public string? RiderEmail { get; set; }
        public string? GuardianFirstName { get; set; }
        public string? GuardianLastName { get; set; }
        public string? GuardianEmail { get; set; }
        public DateTime? PhotoTakenAt { get; set; }
    }
}
