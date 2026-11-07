namespace Services.Repositories.Data.RiderData
{
    /// <summary>
    /// Everything one Gate Sale "another rider" line writes at sale time (Script0309): the rider's own
    /// account, and for a minor the guardian's account and the dependent row linking them.
    /// </summary>
    public sealed class OtherRiderProfileWrite
    {
        public RiderAccountWrite? Rider { get; init; }
        /// <summary>Set for a minor: the guardian's account (then a dependent row is written).</summary>
        public RiderAccountWrite? Guardian { get; init; }
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public DateTime? Birthdate { get; init; }
        public string? EmergencyContactName { get; init; }
        public string? EmergencyContactPhone { get; init; }
        public Guid? CreatedByUserId { get; init; }
    }
}
