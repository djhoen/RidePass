namespace Services.Repositories.Data.PaymentData
{
    public class TenantWaiver
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public int Version { get; set; }
        // Human-readable label so admins can tell waivers apart in dropdowns.
        public string Name { get; set; } = "Waiver";
        public string Title { get; set; } = null!;
        public string Body { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        // Optional cutoff. Past expiry → not offered for new event attachments.
        public DateTime? ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class RiderWaiverSignature
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        // Null for spectator (guest) signatures captured at purchase time.
        public Guid? UserId { get; set; }
        public Guid WaiverId { get; set; }
        public DateTime SignedAt { get; set; }
        public string? IpAddress { get; set; }
        public string? SignatureDataUrl { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentName { get; set; }
        public string? ParentPhone { get; set; }
        // Spectator-specific (guest signatures captured during a spectator buy):
        public string? SignerEmail { get; set; }
        public string? SignerName { get; set; }
        public string? SpectatorFirstName { get; set; }
        public string? SpectatorLastName { get; set; }
        public DateTime? SpectatorBirthdate { get; set; }
    }
}
