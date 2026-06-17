namespace Services.Repositories.Data.UserData
{
    /// <summary>A rider's link from their RidePass account to their LoamMx (LoamPassMx) account.</summary>
    public class RiderLoampassLink
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid TenantId { get; set; }
        public string LoampassAccountId { get; set; } = null!;
        public string LoampassEmail { get; set; } = null!;
        public DateTime LinkedAtUtc { get; set; }
    }
}
