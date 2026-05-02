namespace Services.Repositories.Data.NotificationData
{
    public class Notification
    {
        public Guid Id { get; set; }
        public Guid RecipientUserId { get; set; }
        public Guid? TenantId { get; set; }
        public string Kind { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string Body { get; set; } = null!;
        public string? LinkUrl { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }
}
