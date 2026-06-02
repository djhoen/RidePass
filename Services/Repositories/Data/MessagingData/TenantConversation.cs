namespace Services.Repositories.Data.MessagingData
{
    public class TenantConversation
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string CustomerPhone { get; set; } = null!;
        public Guid? CustomerUserId { get; set; }
        public DateTime LastMessageAt { get; set; }
        public DateTime? LastInboundAt { get; set; }
        public DateTime? LastReadAt { get; set; }
        public string Status { get; set; } = "active";
        public DateTime CreatedAt { get; set; }

        // Derived: unread if customer wrote after admin's last read.
        public bool IsUnread =>
            LastInboundAt.HasValue
            && (!LastReadAt.HasValue || LastInboundAt > LastReadAt);
    }
}
