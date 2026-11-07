namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>The rider profile a Gate Sale "another rider" line is sold against.</summary>
    public class CounterOtherRiderResponse
    {
        /// <summary>The rider's own account (an adult, or a minor with an email). Goes on the cart line.</summary>
        public Guid? RiderUserId { get; set; }
        /// <summary>A minor's link to their guardian. Goes on the cart line.</summary>
        public Guid? RiderDependentId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateOnly? Birthdate { get; set; }
        public int? Age { get; set; }
        public bool IsMinor { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }

        public Guid? GuardianUserId { get; set; }
        public string? GuardianName { get; set; }
        public string? GuardianEmail { get; set; }
        public string? GuardianPhone { get; set; }

        /// <summary>An account with that email already existed and was reused (never duplicated).</summary>
        public bool MatchedExistingRider { get; set; }
        public bool MatchedExistingGuardian { get; set; }
        public bool CreatedRider { get; set; }
        public bool CreatedGuardian { get; set; }
    }
}
