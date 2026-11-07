namespace Services.Repositories.Data.RiderData
{
    /// <summary>
    /// A tenant's gate photo of one rider (rider_photo, Script0307): keyed by the buying account (or,
    /// with no account, the buyer email) plus the normalized rider name. StorageKey is a key into the
    /// image store's PRIVATE area, never a public URL.
    /// </summary>
    public class RiderPhoto
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string OwnerKey { get; set; } = "";
        public Guid? UserId { get; set; }
        public string? OwnerEmail { get; set; }
        public string RiderKey { get; set; } = "";
        public string StorageKey { get; set; } = "";
        public DateTime TakenAt { get; set; }
        public Guid? TakenByUserId { get; set; }
    }
}
