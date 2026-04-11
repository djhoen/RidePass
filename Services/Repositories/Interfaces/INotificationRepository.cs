using Services.Repositories.Data.NotificationData;

namespace Services.Repositories.Interfaces
{
    public interface INotificationRepository
    {
        Task<List<int>> BulkCreateNotifications(List<string> recipientUserIds, Notification templateNotification);
        Task<int> CreateNotification(Notification notification);
        Task<List<Notification>> GetUserNotifications(string userId, int page);
        Task<int> GetUserUnreadNotificationCount(string userId);
        Task ReadUserNotifications(string userId);
    }
}
