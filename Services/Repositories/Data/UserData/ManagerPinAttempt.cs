namespace Services.Repositories.Data.UserData
{
    // Failed manager-PIN attempt state for one (tenant, staff user), backing the brute-force lockout.
    public class ManagerPinAttempt
    {
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public int FailedCount { get; set; }
        public DateTime? LockedUntilUtc { get; set; }
    }
}
