namespace Services.Repositories.Data.RiderData
{
    /// <summary>The profiles one "another rider" line was written to.</summary>
    public sealed record OtherRiderProfileIds(Guid? RiderUserId, Guid? DependentId, Guid? GuardianUserId);
}
