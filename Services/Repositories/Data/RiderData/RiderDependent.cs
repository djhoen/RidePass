namespace Services.Repositories.Data.RiderData
{
    /// <summary>
    /// A minor linked to their parent / guardian's account (Script0309), created when a Gate Sale buys a
    /// ticket for them. Keyed by the gate's rider key under the guardian, so one child is one row per
    /// guardian per tenant. RiderUserId is set when the minor also has an account of their own.
    /// </summary>
    public class RiderDependent
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid GuardianUserId { get; set; }
        public Guid? RiderUserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime? Birthdate { get; set; }
        public string RiderKey { get; set; } = string.Empty;
        /// <summary>The rider's emergency contact: the guardian unless staff entered another.</summary>
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
