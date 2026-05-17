using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IEventTicketTierRepository
    {
        Task<List<EventTicketTier>> GetForEvent(Guid eventId, Guid tenantId, bool activeOnly);
        Task<Dictionary<Guid, List<EventTicketTier>>> GetForEvents(IEnumerable<Guid> eventIds, Guid tenantId, bool activeOnly);
        Task<EventTicketTier?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(EventTicketTier tier);
        Task Update(EventTicketTier tier);
        Task Delete(Guid id, Guid tenantId);
        Task<int> SoldCount(Guid tierId);

        /// <summary>Atomic bulk update of sort_order for many tiers within one event.</summary>
        Task UpdateSortOrders(Guid tenantId, Guid eventId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);
    }
}
