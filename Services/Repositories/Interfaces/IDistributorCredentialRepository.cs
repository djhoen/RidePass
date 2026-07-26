using Services.Repositories.Data.BikeShopData;

namespace Services.Repositories.Interfaces
{
    public interface IDistributorCredentialRepository
    {
        /// <summary>The full row including ciphertext. For the SYNC only; never for a response.</summary>
        Task<TenantDistributorCredential?> Get(Guid tenantId, string distributor);

        /// <summary>Safe projection for the settings screen: no secrets, no ciphertext.</summary>
        Task<List<DistributorConnectionStatus>> ListStatuses(Guid tenantId);

        /// <summary>
        /// Connect or re-connect. Secrets are passed already encrypted. A null secret LEAVES THE
        /// STORED ONE ALONE, so an admin editing the account number doesn't have to re-key a
        /// credential the UI never showed them in the first place.
        /// </summary>
        Task Upsert(Guid tenantId, string distributor, string? accountNumber, string? username,
            string? passwordEncrypted, string? apiKeyEncrypted, bool isEnabled);

        Task Delete(Guid tenantId, string distributor);

        /// <summary>
        /// Every enabled credential across all tenants that hasn't synced since <paramref name="staleBefore"/>,
        /// oldest first. Tenant-spanning BY DESIGN: this is the background sweep's work queue and
        /// runs with no tenant context. Each returned row carries its own TenantId, and everything
        /// downstream scopes by that.
        /// </summary>
        Task<List<TenantDistributorCredential>> ListDueForSync(DateTime staleBefore, int limit = 50);

        Task MarkRunning(Guid id);
        Task MarkResult(Guid id, string status, string? error, int productsSeen, int variantsUpdated);
    }
}
