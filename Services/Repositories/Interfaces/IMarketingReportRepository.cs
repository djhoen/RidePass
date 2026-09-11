using Services.Repositories.Data.NewsletterData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Campaign and automation results: opens, clicks, and purchases attributed to a send.</summary>
    public interface IMarketingReportRepository
    {
        /// <summary>The headline numbers for one campaign. windowDays = how long after the send a purchase still counts.</summary>
        Task<CampaignReportTotals> GetCampaignTotals(Guid campaignId, Guid tenantId, int windowDays);

        /// <summary>
        /// One row per send with what followed. filter: all | opened | clicked | bought | skipped.
        /// search matches email or name. Total on each row is the filtered count before paging.
        /// </summary>
        Task<List<CampaignRecipientRow>> ListCampaignRecipients(Guid campaignId, Guid tenantId, int windowDays,
            string? search, string filter, int skip, int take);

        /// <summary>Purchases attributed to each sent campaign, for the list. Key = campaign id.</summary>
        Task<Dictionary<Guid, MarketingConversion>> GetCampaignConversions(Guid tenantId, int windowDays);

        /// <summary>Purchases attributed to each step of an automation. Key = step id.</summary>
        Task<Dictionary<Guid, MarketingConversion>> GetAutomationStepConversions(Guid automationId, Guid tenantId, int windowDays);
    }
}
