using Services.Repositories.Data.NewsletterData;

namespace Services.Repositories.Interfaces
{
    public interface INewsletterRepository
    {
        Task<List<NewsletterSubscriber>> ListByTenant(Guid tenantId, bool includeUnsubscribed);
        Task<NewsletterSubscriber?> GetByEmail(Guid tenantId, string email);
        Task<NewsletterSubscriber?> GetByUnsubscribeToken(Guid token);
        Task<NewsletterSubscriber> UpsertFromSignup(Guid tenantId, string email, string? name, string source);
        Task<bool> InsertFromImport(Guid tenantId, string email, string? name);
        Task Unsubscribe(Guid id);
        Task Resubscribe(Guid id);
        Task Delete(Guid id, Guid tenantId);
        Task<int> CountActive(Guid tenantId);
        Task<List<NewsletterSubscriber>> ListActiveForSend(Guid tenantId);
    }
}
