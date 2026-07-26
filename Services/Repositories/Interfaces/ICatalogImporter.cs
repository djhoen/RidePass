using Services.Repositories.Data.BikeShopData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// The one catalog operation the distributor sync needs, split out of IBikeShopRepository so
    /// the sync can be tested.
    ///
    /// IBikeShopRepository has ~180 members; a test that only cares whether a sync stamps the right
    /// provenance cannot reasonably fake all of them, and there is no mocking package in this
    /// solution. Depending on the narrow thing instead means the orchestration, including the
    /// licensing guard that keeps distributor content out of the shared parts library, is covered
    /// by a real test rather than by reading the code and hoping.
    ///
    /// IBikeShopRepository inherits this, so production wiring is unchanged.
    /// </summary>
    public interface ICatalogImporter
    {
        /// <summary>
        /// Commit a parsed catalog. Creates by default; with <see cref="ShopImportOptions.UpdateExisting"/>
        /// it matches rows to existing variants (barcode, then MPN, then SKU) and updates them in
        /// place, writing only the columns the source carried.
        /// </summary>
        Task<ShopImportResult> ImportCatalog(Guid tenantId, List<ShopImportProduct> products,
            Guid? byUserId, ShopImportOptions? options = null);
    }
}
