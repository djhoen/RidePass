namespace Services.Repositories.Data.MessagingData
{
    public class TenantSmsOptOut
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Phone { get; set; } = null!;
        public bool OptedOut { get; set; }
        public DateTime? OptedOutAt { get; set; }
        public DateTime? OptedInAt { get; set; }
        public string? LastKeyword { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
