namespace Services.Repositories.Data.NotificationData
{
    public class Notification
    {
        public int Id { get; set; }
        public string Subject { get; set; }
        public string? Body { get; set; }
        public string? ActionText { get; set; }
        public string? ActionUrl { get; set; }
        public string? FromUserId { get; set; }
        public string RecipientUserId { get; set; }
        public int NotificationTypeId { get; set; }
        public bool Read { get; set; }
        public DateTime NotificationDate { get; set; }
        public string? NotificationDateString { get; set; }
    }

    public enum NotificationType
    {
        Unknown = 0,
        General = 1,
        Marketing = 2,
        System = 3
    }
}
