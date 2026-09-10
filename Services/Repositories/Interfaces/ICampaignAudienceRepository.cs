using Services.Repositories.Data.NewsletterData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Resolves a broadcast campaign's audience to recipients. Every query is scoped to the
    /// tenant; a target id from another tenant resolves to nothing, never to their riders.
    /// </summary>
    public interface ICampaignAudienceRepository
    {
        /// <summary>Distinct recipients (by lower-cased email) for the audience. Not yet suppression-filtered.</summary>
        Task<List<CampaignAudienceRecipient>> ListRecipients(Guid tenantId, string kind, CampaignAudienceConfig config);

        /// <summary>The events, event types, and pass products the editor may target.</summary>
        Task<CampaignAudienceOptions> ListOptions(Guid tenantId);

        /// <summary>
        /// Human label for the audience ("Purchasers of Spring Camp"), or null when the target
        /// does not exist in this tenant, which the API treats as a validation failure.
        /// </summary>
        Task<string?> DescribeAudience(Guid tenantId, string kind, CampaignAudienceConfig config);
    }
}
