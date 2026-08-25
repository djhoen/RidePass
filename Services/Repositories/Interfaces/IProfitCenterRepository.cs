using Services.Repositories.Data.AccountingData;

namespace Services.Repositories.Interfaces
{
    public interface IProfitCenterRepository
    {
        Task<List<ProfitCenter>> ListForTenant(Guid tenantId);
        Task<List<ProfitCenterAssignment>> ListAssignments(Guid tenantId);
        Task<ProfitCenter?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(Guid tenantId, string name, int sortOrder, string color);
        /// <summary>Name and color together: the edit form saves both at once.</summary>
        Task Update(Guid id, Guid tenantId, string name, string color);
        Task Delete(Guid id, Guid tenantId);
        Task UpdateSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);
        /// <summary>Assigns a revenue slot to a center. No-op if the center is not this tenant's.</summary>
        Task UpsertAssignment(Guid tenantId, string revenueKey, Guid profitCenterId);
        Task ClearAssignment(Guid tenantId, string revenueKey);
    }
}
