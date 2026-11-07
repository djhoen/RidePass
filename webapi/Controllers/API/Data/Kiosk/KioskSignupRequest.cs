namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>A walk-up's own kiosk sign-up: who they are, a guardian when under 18, the signed waiver and photo.</summary>
    public class KioskSignupRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        /// <summary>Required for 18 and over; optional for a minor (then they are linked to the guardian only).</summary>
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public DateTime? Birthdate { get; set; }

        public string? GuardianFirstName { get; set; }
        public string? GuardianLastName { get; set; }
        public string? GuardianEmail { get; set; }
        public string? GuardianPhone { get; set; }
        public DateTime? GuardianBirthdate { get; set; }

        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }

        /// <summary>The signer ticked "I have read and agree" (the guardian for a minor).</summary>
        /// <summary>The waiver the kiosk showed: refused if it is no longer the active one.</summary>
        public Guid? WaiverId { get; set; }

        public bool AgreedToWaiver { get; set; }
        public string? SignatureDataUrl { get; set; }
        public string? PhotoDataUrl { get; set; }
    }
}
