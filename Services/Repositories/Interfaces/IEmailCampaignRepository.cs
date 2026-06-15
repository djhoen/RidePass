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
        Task MarkScheduled(Guid id, DateTime scheduledForUtc);
        Task RevertToDraft(Guid id);
        Task DeleteSendRows(Guid campaignId);
        Task CreateSendRows(Guid campaignId, IEnumerable<EmailCampaignSend> sends);
        Task<List<EmailCampaignSend>> ListSends(Guid campaignId);
        Task UpdateSendStatus(Guid sendId, string status, string? error);
        // Count of emails this tenant has SENT since `fromUtc`, excluding one campaign
        // (used to apply cumulative monthly pricing tiers to a fresh send).
        Task<int> CountSentEmailsInMonth(Guid tenantId, DateTime fromUtc, Guid excludeCampaignId);
    }
}
