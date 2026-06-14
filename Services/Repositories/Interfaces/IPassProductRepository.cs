using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IPassProductRepository
    {
        Task<List<PassProduct>> GetAllForTenant(Guid tenantId, bool activeOnly);

        /// <summary>
        /// True when another ACTIVE pass product in the tenant already uses this name
        /// (case-insensitive), excluding the given id. Blocks duplicate-named passes,
        /// which otherwise show up as repeated rows on the event pricing pages.
        /// </summary>
        Task<bool> ExistsActiveByName(Guid tenantId, string name, Guid excludeId);

        Task<PassProduct?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(PassProduct product);
        Task Update(PassProduct product);
        Task Delete(Guid id, Guid tenantId);

        /// <summary>Atomic bulk update of sort_order for many pass products at once.</summary>
        Task UpdateSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);
    }
}
