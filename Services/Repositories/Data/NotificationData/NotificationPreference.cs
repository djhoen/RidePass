namespace Services.Repositories.Data.NotificationData
{
    public class NotificationPreference
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Kind { get; set; } = null!;
        public bool EmailEnabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
