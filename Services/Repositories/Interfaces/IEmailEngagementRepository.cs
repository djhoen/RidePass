using Services.Repositories.Data.NewsletterData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Opens and clicks reported by SendGrid, keyed back to our send rows. Every read is scoped
    /// by tenant; the write comes from the webhook, which resolves the tenant from the event's
    /// unique args before it gets here.
    /// </summary>
    public interface IEmailEngagementRepository
    {
        /// <summary>Insert one event; a repeat of the same SendGrid event id is ignored.</summary>
        Task Record(EmailEngagement e);

        /// <summary>Per campaign: distinct openers, distinct clickers, total clicks.</summary>
        Task<Dictionary<Guid, EmailEngagementStats>> GetCampaignStats(Guid tenantId);

        /// <summary>Which links in one campaign were clicked, most clicked first.</summary>
        Task<List<EmailClickUrlStats>> GetCampaignClickUrls(Guid campaignId, Guid tenantId);

        /// <summary>Per automation step, for one automation.</summary>
        /// <summary>Per automation, all steps together, for the list. Key = automation id.</summary>
        Task<Dictionary<Guid, EmailEngagementStats>> GetAutomationStats(Guid tenantId);
        Task<Dictionary<Guid, EmailEngagementStats>> GetAutomationStepStats(Guid automationId, Guid tenantId);
    }

    public interface IEmailTemplateRepository
    {
        Task<List<EmailTemplate>> ListForTenant(Guid tenantId);
        Task<EmailTemplate?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(EmailTemplate t);
        Task Update(EmailTemplate t);
        Task Delete(Guid id, Guid tenantId);
    }
}
