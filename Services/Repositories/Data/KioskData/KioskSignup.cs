namespace Services.Repositories.Data.KioskData
{
    /// <summary>One completed kiosk sign-up (Script0310): the profile it made or used, the signature and photo.</summary>
    public class KioskSignup
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? KioskDeviceId { get; set; }
        /// <summary>The rider's own account (an adult, or a minor who gave an email).</summary>
        public Guid? UserId { get; set; }
        /// <summary>A minor's link to their guardian.</summary>
        public Guid? DependentId { get; set; }
        public Guid? GuardianUserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime? Birthdate { get; set; }
        public bool IsMinor { get; set; }
        public Guid? SignatureId { get; set; }
        public Guid? PhotoId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? HandledAt { get; set; }
        public Guid? HandledByUserId { get; set; }
        public string? SaleRef { get; set; }
        public Guid? SaleTicketId { get; set; }
        public DateTime? DismissedAt { get; set; }
        public Guid? DismissedByUserId { get; set; }

        // ── Held for staff (the email matched an existing account): nothing written to it yet ──
        public bool MatchPending { get; set; }
        public Guid? MatchedUserId { get; set; }
        public Guid? MatchedGuardianUserId { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? GuardianFirstName { get; set; }
        public string? GuardianLastName { get; set; }
        public string? GuardianEmail { get; set; }
        public string? GuardianPhone { get; set; }
        public DateTime? GuardianBirthdate { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? HeldSignatureDataUrl { get; set; }
        public Guid? HeldWaiverId { get; set; }
        public DateTime? HeldSignedAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public Guid? ConfirmedByUserId { get; set; }
    }
}
