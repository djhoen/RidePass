using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IDayPassProductRepository
    {
        Task<List<DayPassProduct>> GetAllForTenant(Guid tenantId, bool activeOnly);
        Task<DayPassProduct?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(DayPassProduct product);
        Task Update(DayPassProduct product);
        Task Delete(Guid id, Guid tenantId);
    }
}
