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
        public DateTime? DismissedAt { get; set; }
        public Guid? DismissedByUserId { get; set; }
    }
}
