using Services.Repositories.Data.TenantData;

namespace Services.Repositories.Interfaces
{
    public interface ITenantEventTypeRepository
    {
        Task<List<TenantEventType>> GetAllForTenant(Guid tenantId);
        Task<TenantEventType?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(TenantEventType type);
        Task Update(Guid id, Guid tenantId, string name, string color, string? imageUrl, int sortOrder);
        Task Delete(Guid id, Guid tenantId);
        Task<bool> IsInUseByEvents(Guid id, Guid tenantId);

        /// <summary>Set whether Loam Pass credits are accepted for entry to this event type.</summary>
        Task SetLoampassRedemption(Guid id, Guid tenantId, bool allow);

        /// <summary>Atomic bulk update of sort_order for many event types at once.</summary>
        Task UpdateSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);
    }
}
