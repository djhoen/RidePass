using Services.Repositories.Data.EventData;

namespace Services.Notifications
{
    /// <summary>
    /// Fans out a "new event published" notification to every active subscriber of the
    /// tenant. Intended to be called fire-and-forget from the event create path so the
    /// admin's request returns immediately.
    /// </summary>
    public interface IEventNotifier
    {
        Task NotifyNewEvent(Guid tenantId, Event ev);
    }
}
