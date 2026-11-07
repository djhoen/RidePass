namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>
    /// The draft rider profile an "another rider" line will be sold against: who it is, and which existing
    /// profiles the sale will use or which new ones it will create. Nothing exists yet for a Will* profile.
    /// </summary>
    public class CounterOtherRiderResponse
    {
        /// <summary>The rider's existing account, if any (a minor's is the one their dependent row keeps).</summary>
        public Guid? RiderUserId { get; set; }
        /// <summary>The existing dependent row (a minor linked to this guardian), if any.</summary>
        public Guid? RiderDependentId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateOnly? Birthdate { get; set; }
        public int? Age { get; set; }
        public bool IsMinor { get; set; }
        public string? Email { get; set; }

        public Guid? GuardianUserId { get; set; }
        public string? GuardianName { get; set; }
        public string? GuardianEmail { get; set; }
        public string? GuardianPhone { get; set; }

        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }

        /// <summary>An account with that email already exists and will be used (never duplicated).</summary>
        public bool MatchedExistingRider { get; set; }
        public bool MatchedExistingGuardian { get; set; }
        public bool WillCreateRider { get; set; }
        public bool WillCreateGuardian { get; set; }
        public bool WillCreateDependent { get; set; }
    }
}
