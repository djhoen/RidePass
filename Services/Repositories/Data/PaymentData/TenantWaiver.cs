namespace Services.Repositories.Data.PaymentData
{
    public class TenantWaiver
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public int Version { get; set; }
        public string Title { get; set; } = null!;
        public string Body { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class RiderWaiverSignature
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public Guid WaiverId { get; set; }
        public DateTime SignedAt { get; set; }
        public string? IpAddress { get; set; }
    }
}
