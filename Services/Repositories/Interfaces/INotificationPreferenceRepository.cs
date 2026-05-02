using Services.Repositories.Data.NotificationData;

namespace Services.Repositories.Interfaces
{
    public interface INotificationPreferenceRepository
    {
        Task<List<NotificationPreference>> ListForUser(Guid userId);
        Task<bool> IsEmailEnabled(Guid userId, string kind);
        Task Upsert(Guid userId, string kind, bool emailEnabled);
    }
}
