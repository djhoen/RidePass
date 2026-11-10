using Services.Repositories.Data.PricingData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Peak-day price rules (Script0316). Tenant-scoped.</summary>
    public interface IDayPriceRuleRepository
    {
        Task<List<DayPriceRule>> List(Guid tenantId, bool activeOnly);
        Task<Guid> Create(DayPriceRule rule);
        Task<int> SetActive(Guid tenantId, Guid id, bool isActive);
        Task<int> Delete(Guid tenantId, Guid id);
    }
}
