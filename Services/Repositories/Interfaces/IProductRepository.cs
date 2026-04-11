using Services.Repositories.Data.ProductData;

namespace Services.Repositories.Interfaces
{
    public interface IProductRepository
    {
        Task<int> CreateProductOffer(ProductOffer offer);
        Task<List<Product>> GetAllProducts();
        Task<List<ProductOffer>> GetAllProductOffers();
        Task<Product> GetProduct(int productId);
        Task<List<ProductBundleItem>> GetProductBundleItems(int productId);
        Task<List<Product>> GetProducts(bool activeOnly = true);
        Task UpdateProductOffer(ProductOffer offer);
    }
}
