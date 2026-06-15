using Services.Repositories.Data.ConcessionData;

namespace Services.Repositories.Interfaces
{
    public interface IConcessionRepository
    {
        // Products
        Task<List<ConcessionProduct>> ListProducts(Guid tenantId, bool activeOnly);
        Task<ConcessionProduct?> GetProduct(Guid id, Guid tenantId);
        Task<Guid> CreateProduct(ConcessionProduct p);
        Task UpdateProduct(ConcessionProduct p);
        Task DeleteProduct(Guid id, Guid tenantId);
        Task UpdateProductSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);

        // Variants
        Task<List<ConcessionVariant>> ListVariants(Guid productId);
        Task<Dictionary<Guid, List<ConcessionVariant>>> ListVariantsForProducts(IEnumerable<Guid> productIds);
        Task<ConcessionVariant?> GetVariant(Guid id);
        Task<Guid> CreateVariant(ConcessionVariant v);
        Task UpdateVariant(ConcessionVariant v);
        Task DeleteVariant(Guid id);

        // Sold counts (pending + paid reserve stock) for inventory checks.
        Task<Dictionary<Guid, int>> SumSoldVariants(IEnumerable<Guid> variantIds);
        Task<int> SumSoldVariant(Guid variantId);

        // Sales
        Task<Guid> CreateSale(ConcessionSale sale);
        Task CreateSaleLines(Guid saleId, IEnumerable<ConcessionSaleLine> lines);
        Task SetSalePaymentIntentId(Guid saleId, string paymentIntentId);
        Task<ConcessionSale?> GetSaleByPaymentIntentId(string paymentIntentId);
        Task<ConcessionSale?> GetSale(Guid id, Guid tenantId);
        Task MarkSalePaid(Guid saleId);
        Task MarkSaleFailed(Guid saleId);
    }
}
