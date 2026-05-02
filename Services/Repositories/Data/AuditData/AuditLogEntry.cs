namespace Services.Repositories.Data.AuditData
{
    public class AuditLogEntry
    {
        public Guid Id { get; set; }
        public Guid? ActorUserId { get; set; }
        public string? ActorEmail { get; set; }
        public string? ActorRole { get; set; }
        public string Action { get; set; } = null!;
        public string? TargetKind { get; set; }
        public Guid? TargetId { get; set; }
        public string Summary { get; set; } = null!;
        public string? Metadata { get; set; }   // raw jsonb text for simplicity
        public string? IpAddress { get; set; }
        public Guid? TenantId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
