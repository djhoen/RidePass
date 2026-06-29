using Services.Repositories.Data.TaxData;

namespace Services.Repositories.Interfaces
{
    public interface ITenantTaxRepository
    {
        // The tenant's rate for a kind ('admission'), or null when none is configured (= no tax).
        Task<TenantTaxRate?> GetByKind(Guid tenantId, string taxKind);

        // Insert-or-update the single (tenant, kind) row and return the stored result.
        Task<TenantTaxRate> Upsert(TenantTaxRate rate);
    }
}
