using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IPassProductRepository
    {
        Task<List<PassProduct>> GetAllForTenant(Guid tenantId, bool activeOnly);
        Task<PassProduct?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(PassProduct product);
        Task Update(PassProduct product);
        Task Delete(Guid id, Guid tenantId);

        /// <summary>Atomic bulk update of sort_order for many pass products at once.</summary>
        Task UpdateSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);
    }
}
