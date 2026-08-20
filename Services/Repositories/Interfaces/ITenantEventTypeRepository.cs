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

        /// <summary>
        /// Does this tenant point ANY event type at the given QuickBooks revenue slot
        /// (tenant_event_type.revenue_key, Script0274)? Existence only: the QuickBooks settings
        /// screen asks this on every status/mapping load just to decide whether to require the
        /// slot, so it must stay a single indexed-scan EXISTS, never a list load.
        /// </summary>
        Task<bool> AnyWithRevenueKey(Guid tenantId, string revenueKey);

        /// <summary>Set whether Loam Pass credits are accepted for entry to this event type.</summary>
        Task SetLoampassRedemption(Guid id, Guid tenantId, bool allow);

        /// <summary>Atomic bulk update of sort_order for many event types at once.</summary>
        Task UpdateSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);
    }
}
