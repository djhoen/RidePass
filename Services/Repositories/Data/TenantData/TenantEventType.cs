namespace Services.Repositories.Data.TenantData
{
    public class TenantEventType
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Color { get; set; } = "#1976D2";
        public string? ImageUrl { get; set; }
        public int SortOrder { get; set; }
        public bool IsSystem { get; set; }
        // Whether a Loam Pass credit may be redeemed for entry to events of this type, at a
        // LoamPassMx track. Practice is always allowed regardless of this flag (enforced in code).
        public bool AllowLoampassRedemption { get; set; }
        // Which QuickBooks revenue slot this type's ticket (and event-extra) revenue posts to.
        // NULL = whatever the source kind implies (Script0274); the DB CHECK limits the values.
        public string? RevenueKey { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
