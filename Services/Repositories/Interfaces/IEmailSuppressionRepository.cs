using Services.Repositories.Data.EmailData;

namespace Services.Repositories.Interfaces
{
    public interface IEmailSuppressionRepository
    {
        /// <summary>Add (or no-op if already present) a suppression entry.</summary>
        Task Suppress(Guid? tenantId, string email, string reason, string scope, string? source, string? detail);

        /// <summary>
        /// True if this address must NOT receive a send. Always blocks when an 'all'-scope
        /// row exists (global or this tenant). When <paramref name="marketing"/> is true,
        /// also blocks on a 'marketing'-scope row. Transactional sends pass marketing=false
        /// so receipts/verification still go out to addresses that only opted out of marketing.
        /// </summary>
        Task<bool> IsSuppressed(string email, Guid? tenantId, bool marketing);

        /// <summary>
        /// The set of lowercased addresses that must NOT receive marketing for this tenant:
        /// every 'all'-scope row (hard bounces, global or tenant) plus every 'marketing'-scope
        /// row (global or tenant). Fetched once so a bulk send can filter in memory instead of
        /// querying per recipient.
        /// </summary>
        Task<HashSet<string>> ListMarketingBlocklist(Guid tenantId);

        Task<List<EmailSuppression>> ListForTenant(Guid tenantId, int take = 500);

        /// <summary>Remove a tenant-scoped suppression (e.g. admin re-enables an address).</summary>
        Task RemoveForTenant(Guid id, Guid tenantId);
    }
}
