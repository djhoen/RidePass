using Services.Repositories.Data.NewsletterData;

namespace Services.Repositories.Interfaces
{
    public interface IEmailCampaignRepository
    {
        Task<List<EmailCampaign>> ListByTenant(Guid tenantId);
        Task<EmailCampaign?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(EmailCampaign campaign);
        Task Update(EmailCampaign campaign);
        Task Delete(Guid id, Guid tenantId);
        Task MarkSent(Guid id, int recipientCount);
        Task MarkSending(Guid id);
        Task CreateSendRows(Guid campaignId, IEnumerable<EmailCampaignSend> sends);
    }
}
