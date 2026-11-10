namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>A kiosk sign-up as the gate screens list it (staff only).</summary>
    public class KioskSignupItem
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateOnly? Birthdate { get; set; }
        public int? Age { get; set; }
        public bool IsMinor { get; set; }
        public Guid? RiderUserId { get; set; }
        public string? RiderEmail { get; set; }
        public Guid? DependentId { get; set; }
        public Guid? GuardianUserId { get; set; }
        public string? GuardianName { get; set; }
        public string? GuardianEmail { get; set; }
        public bool WaiverSigned { get; set; }
        public Guid? PhotoId { get; set; }
        public DateTime? PhotoTakenAtUtc { get; set; }
        public string? KioskName { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? HandledAtUtc { get; set; }
        public DateTime? DismissedAtUtc { get; set; }

        /// <summary>The email matched an existing account: held until a cashier checks ID and confirms.</summary>
        public bool MatchPending { get; set; }
        /// <summary>The existing account(s) it matched (staff only).</summary>
        public string? MatchedAccountName { get; set; }
        public string? MatchedGuardianName { get; set; }
        /// <summary>A photo is already on file for the matched rider (offer "Replace photo on file").</summary>
        public bool MatchedHasPhoto { get; set; }
        /// <summary>For the ID check (pending sign-ups only): the birthdate typed at the kiosk for the person whose
        /// ID is checked (the guardian's for a minor) and the one on the matched account.</summary>
        public DateOnly? TypedIdBirthdate { get; set; }
        public DateOnly? MatchedIdBirthdate { get; set; }
        /// <summary>What was typed at the kiosk (pending sign-ups).</summary>
        public string? TypedEmail { get; set; }
    }
}
