using Services.Repositories.Data.EventData;

namespace Services.Repositories.Interfaces
{
    public interface IEventRepository
    {
        Task<List<Event>> GetInRange(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<Event?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(Event ev);
        Task Update(Event ev);
        Task Delete(Guid id, Guid tenantId);
        Task<List<EventWithTypeContext>> GetUpcomingWithType(Guid tenantId, int limit);
    }
}
