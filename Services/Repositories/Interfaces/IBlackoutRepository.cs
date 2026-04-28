using Services.Repositories.Data.EventData;

namespace Services.Repositories.Interfaces
{
    public interface IBlackoutRepository
    {
        Task<List<Blackout>> GetInRange(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<Blackout?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(Blackout blackout);
        Task Update(Blackout blackout);
        Task Delete(Guid id, Guid tenantId);
    }
}
