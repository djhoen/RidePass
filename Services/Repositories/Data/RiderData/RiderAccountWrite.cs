using Services.Repositories.Data.UserData;

namespace Services.Repositories.Data.RiderData
{
    /// <summary>
    /// One account a Gate Sale "another rider" line needs, written when the sale is made: an existing
    /// rider account (only empty fields are filled, never overwritten) or a new platform rider account.
    /// </summary>
    public sealed class RiderAccountWrite
    {
        public Guid? ExistingId { get; init; }
        /// <summary>A new account (built by the shared rider-account helper), when ExistingId is null.</summary>
        public User? NewAccount { get; init; }
        public string? FillPhone { get; init; }
        public DateTime? FillBirthdate { get; init; }
        public string? FillEmergencyContactName { get; init; }
        public string? FillEmergencyContactPhone { get; init; }
    }
}
