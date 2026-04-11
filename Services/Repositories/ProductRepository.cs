using Services.Helpers.Interfaces;
using Services.Repositories.Data.ProductData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class ProductRepository : IProductRepository
    {
        private readonly IDbHelper _dbHelper;
        public ProductRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
        }

        public async Task<int> CreateProductOffer(ProductOffer offer)
        {
            var sql = @"INSERT INTO ""product.offer"" (""shortDescription"", ""longDescription"", ""productId"", ""offerProductId"", ""isActive"")
                       VALUES (@ShortDescription, @LongDescription, @ProductId, @OfferProductId, @IsActive)
                       RETURNING ""id"";";
            var newId = await _dbHelper.Query<int>(sql, offer);
            return newId.FirstOrDefault();
        }

        public async Task<List<Product>> GetAllProducts()
        {
            var sql = @"SELECT * FROM ""product"" ORDER BY ""id""";

            var productResult = await _dbHelper.Query<Product>(sql);
            return productResult.ToList();
        }

        public async Task<List<ProductOffer>> GetAllProductOffers()
        {
            var sql = @"SELECT po.*, p.*
                        FROM ""product.offer"" po
                        INNER JOIN ""product"" p ON p.""id"" = po.""offerProductId""
                        ORDER BY po.""id""";
            var result = await _dbHelper.Query<ProductOffer, Product, ProductOffer>(
                sql,
                (po, p) =>
                {
                    po.OfferProduct = p;
                    return po;
                },
                param: null,
                splitOn: "Id"
            );
            return result.ToList();
        }

        public async Task<Product> GetProduct(int productId)
        {
            var productResult = await _dbHelper.Query<Product>(@"SELECT * FROM ""product"" WHERE ""id"" = @productId", new { productId });

            return productResult.FirstOrDefault();
        }

        public async Task<List<ProductBundleItem>> GetProductBundleItems(int productId)
        {
            var sql = @"SELECT pbi.* FROM ""product.bundleItem"" pbi
                        WHERE pbi.""parentProductId"" = @productId";

            var result = await _dbHelper.Query<ProductBundleItem>(sql, new { productId });
            return result.ToList();
        }

        public async Task<List<Product>> GetProducts(bool activeOnly = true)
        {
            var date = DateTime.Now;
            var sql = @"SELECT * FROM ""product"" WHERE ""endDate"" IS NULL OR ""endDate"" > @date ORDER BY ""id""";

            if (activeOnly)
            {
                sql = @"SELECT * FROM ""product""
                        WHERE ""isActive"" IS TRUE
                            AND (""startDate"" IS NULL OR ""startDate"" < @date)
                            AND (""endDate"" IS NULL OR ""endDate"" > @date)
                        ORDER BY ""id""";
            }

            var productResult = await _dbHelper.Query<Product>(sql, new { date });
            return productResult.ToList();
        }

        public async Task UpdateProductOffer(ProductOffer offer)
        {
            var sql = @"UPDATE ""product.offer""
                       SET ""shortDescription"" = @ShortDescription,
                           ""longDescription"" = @LongDescription,
                           ""productId"" = @ProductId,
                           ""offerProductId"" = @OfferProductId,
                           ""isActive"" = @IsActive
                       WHERE ""id"" = @Id;";
            await _dbHelper.Execute(sql, offer);
        }
    }
}
