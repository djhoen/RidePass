namespace Services.Repositories.Data.KioskData
{
    /// <summary>A paired front-desk tablet (Script0310). The secret and the pairing code are stored hashed.</summary>
    public class KioskDevice
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? CredentialHash { get; set; }
        public string? PairingCodeHash { get; set; }
        public DateTime? PairingExpiresAt { get; set; }
        public DateTime? PairedAt { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public Guid? RevokedByUserId { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
