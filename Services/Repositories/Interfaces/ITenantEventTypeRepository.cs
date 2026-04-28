using Services.Repositories.Data.TenantData;

namespace Services.Repositories.Interfaces
{
    public interface ITenantEventTypeRepository
    {
        Task<List<TenantEventType>> GetAllForTenant(Guid tenantId);
        Task<TenantEventType?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(TenantEventType type);
        Task Update(Guid id, Guid tenantId, string name, string color, int sortOrder);
        Task Delete(Guid id, Guid tenantId);
        Task<bool> IsInUseByEvents(Guid id, Guid tenantId);
    }
}
