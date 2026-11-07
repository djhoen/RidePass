namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>
    /// "Another rider" on a Gate Sale, as the cashier entered them: an existing profile (ExistingUserId /
    /// ExistingDependentId) or a new rider; a minor needs a guardian (the paying customer, an existing
    /// account, or name + email (+ phone)), who must be 18 or over. Sent with the cart line: nothing is
    /// created or changed until the sale is made.
    /// </summary>
    public class CounterOtherRiderInput
    {
        public Guid? ExistingUserId { get; set; }
        public Guid? ExistingDependentId { get; set; }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? Birthdate { get; set; }
        /// <summary>The rider's own email: required for a new adult, optional for a minor.</summary>
        public string? Email { get; set; }
        public string? Phone { get; set; }

        /// <summary>The paying customer is this minor's parent / guardian.</summary>
        public bool GuardianIsPayer { get; set; }
        public Guid? GuardianUserId { get; set; }
        public string? GuardianFirstName { get; set; }
        public string? GuardianLastName { get; set; }
        public string? GuardianEmail { get; set; }
        public string? GuardianPhone { get; set; }
        /// <summary>Required when the guardian's age isn't on file (a new guardian, or an account without one).</summary>
        public DateTime? GuardianBirthdate { get; set; }

        /// <summary>The rider's emergency contact (tenants that require one). A minor defaults to their guardian.</summary>
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }

        /// <summary>The kiosk sign-up this rider came from (Script0310): its waiver signature goes on the ticket.</summary>
        public Guid? KioskSignupId { get; set; }
    }
}
