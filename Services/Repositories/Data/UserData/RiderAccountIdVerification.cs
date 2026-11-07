namespace Services.Repositories.Data.UserData
{
    /// <summary>
    /// One tenant's record that it checked a rider account's photo ID and birthdate
    /// (rider_id_verification, Script0305). Rider accounts are platform-wide, so the check is kept
    /// per tenant here rather than on users.
    /// </summary>
    public class RiderAccountIdVerification
    {
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        /// <summary>The rider the check was for: their normalized name (RiderIdVerification.RiderKey).</summary>
        public string RiderKey { get; set; } = string.Empty;
        public DateTime VerifiedAt { get; set; }
        public Guid? VerifiedByUserId { get; set; }
        public DateTime? VerifiedDob { get; set; }
    }
}
