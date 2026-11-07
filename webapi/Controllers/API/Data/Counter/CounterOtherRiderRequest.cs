namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>
    /// "Another rider" on a Gate Sale: find or create the rider's own profile (and, for a minor, their
    /// parent / guardian's). Pick an existing profile (ExistingUserId / ExistingDependentId) or type a new
    /// rider; a minor needs a guardian: the paying customer, an existing account, or name + email (+ phone).
    /// </summary>
    public class CounterOtherRiderRequest
    {
        /// <summary>The paying customer (so the rider can't be them, and so they can be the guardian).</summary>
        public Guid PayerUserId { get; set; }

        public Guid? ExistingUserId { get; set; }
        public Guid? ExistingDependentId { get; set; }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? Birthdate { get; set; }
        /// <summary>The rider's own email: required for an adult, optional for a minor.</summary>
        public string? Email { get; set; }
        public string? Phone { get; set; }

        /// <summary>The paying customer is this minor's parent / guardian.</summary>
        public bool GuardianIsPayer { get; set; }
        public Guid? GuardianUserId { get; set; }
        public string? GuardianFirstName { get; set; }
        public string? GuardianLastName { get; set; }
        public string? GuardianEmail { get; set; }
        public string? GuardianPhone { get; set; }
    }
}
