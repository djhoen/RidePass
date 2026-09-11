using Services.Repositories.Data.NewsletterData;

namespace Services.Repositories.Interfaces
{
    public interface IAudienceRepository
    {
        Task<List<Audience>> ListForTenant(Guid tenantId);
        /// <summary>Every audience of every tenant, for the hourly membership refresh.</summary>
        Task<List<Audience>> ListAllAcrossTenants();
        /// <summary>Current members per audience from audience_member (as of the last refresh).</summary>
        Task<Dictionary<Guid, int>> ActiveMemberCounts(Guid tenantId);
        Task<Audience?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(Audience a);
        Task Update(Audience a);
        Task<bool> Delete(Guid id, Guid tenantId);

        /// <summary>Everyone the definition matches right now, one row per inbox.</summary>
        Task<List<AudienceRecipient>> Evaluate(Guid tenantId, AudienceDefinition def);
        Task<int> Count(Guid tenantId, AudienceDefinition def);

        /// <summary>
        /// Bring audience_member up to date: people matching now who were not members join
        /// (joined_at = now), members who no longer match leave. Returns (joined, left).
        /// </summary>
        Task<(int Joined, int Left)> RefreshMembers(Audience a);

        /// <summary>Draft/scheduled campaigns and automations pointing at each audience.</summary>
        Task<Dictionary<Guid, AudienceUsage>> UsageCounts(Guid tenantId);

        /// <summary>Insert the sample audiences this tenant does not have yet (matched by name). Returns how many were added.</summary>
        Task<int> SeedSamples(Guid tenantId, Guid? userId);
    }
}
