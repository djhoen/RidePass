using Services.Repositories.Data.NotificationData;

namespace Services.Repositories.Interfaces
{
    public interface INotificationRepository
    {
        Task<Guid> Insert(Notification n);
        Task<List<Notification>> ListForUser(Guid userId, int take = 50);
        Task<int> CountUnread(Guid userId);
        Task MarkRead(Guid id, Guid userId);
        Task MarkAllRead(Guid userId);
    }
}
