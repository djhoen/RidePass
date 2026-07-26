using Services.Repositories.Data.DiscountData;

namespace Services.Repositories.Interfaces
{
    public interface IDiscountPresetRepository
    {
        /// <summary>Every discount for the tenant, for the settings screen.</summary>
        Task<List<DiscountPreset>> ListForTenant(Guid tenantId, bool activeOnly);

        /// <summary>Active discounts applicable to one surface, for a counter to offer. Filtering
        /// server-side rather than shipping the whole list keeps a counter from showing a discount
        /// it would then be refused for.</summary>
        Task<List<DiscountPreset>> ListForSurface(Guid tenantId, string surface);

        /// <summary>Tenant-scoped single read. Null when it isn't this tenant's.</summary>
        Task<DiscountPreset?> Get(Guid id, Guid tenantId);

        Task<Guid> Create(DiscountPreset p);
        Task<int> Update(DiscountPreset p);
        Task<int> Delete(Guid id, Guid tenantId);
    }
}
