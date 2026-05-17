namespace Services.Repositories.Data.EventData
{
    public class EventSubscription
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
        public bool NotifyEmail { get; set; }
        public bool NotifySms { get; set; }
        public Guid UnsubscribeToken { get; set; }
        public DateTime? UnsubscribedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
