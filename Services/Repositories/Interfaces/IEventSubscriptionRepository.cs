using Services.Repositories.Data.EventData;

namespace Services.Repositories.Interfaces
{
    public interface IEventSubscriptionRepository
    {
        Task<EventSubscription?> GetByTenantAndEmail(Guid tenantId, string email);
        Task<EventSubscription?> GetByUnsubscribeToken(Guid token);
        Task<List<EventSubscription>> ListActiveForTenant(Guid tenantId);
        Task<Guid> Upsert(EventSubscription sub);
        Task SetUnsubscribed(Guid id, bool unsubscribed);
    }
}
