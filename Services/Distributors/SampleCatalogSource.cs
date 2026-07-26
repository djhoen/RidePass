using Microsoft.Extensions.Configuration;
using Services.Repositories.Data.BikeShopData;

namespace Services.Distributors
{
    /// <summary>
    /// A fake distributor that returns a fixed catalog, so the whole sync can be driven end to end
    /// without a dealer account anywhere.
    ///
    /// WHY THIS EXISTS. The QBP transport can't be written until someone has CLS documentation, but
    /// everything around it (credentials, scheduling, mapping, matching, provenance, the settings
    /// screen) is finished and ought to be provable before a real feed is pointed at a live
    /// catalog. Connecting this and pressing Sync now exercises every one of those, and a second
    /// press proves the update path: the same rows are matched and refreshed rather than duplicated.
    ///
    /// OFF BY DEFAULT, and it must stay that way. Set Distributors:EnableSampleSource=true in dev or
    /// staging config. On production the card never appears, so nobody can connect a fake
    /// distributor and pour six invented products into a real shop's inventory.
    ///
    /// ITS NAMES ARE DELIBERATELY NOT POOLABLE. 'sample' is absent from LibraryContribution's
    /// allow-list, exactly as a real distributor's slug is, so a test run demonstrates the licensing
    /// guard rather than bypassing it. If you scan one of these barcodes after syncing and the
    /// shared library stays empty, that is the guard working, not a bug. To watch the library fill,
    /// type a manufacturer name onto a variant by hand or upload a CSV: those sources ARE poolable.
    /// </summary>
    public class SampleCatalogSource : IDistributorCatalogSource
    {
        private readonly IConfiguration _configuration;

        public SampleCatalogSource(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string Slug => "sample";
        public string DisplayName => "Sample Distributor (testing)";
        public string ManufacturerNameSource => Services.BikeShop.ManufacturerNameSource.Sample;

        // Read as a raw string and parsed rather than via GetValue<bool>, which lives in the
        // Configuration.Binder package that Services doesn't reference. An absent or unparseable
        // setting is false, so production (where the key simply isn't there) stays off.
        public bool IsConfigured =>
            bool.TryParse(_configuration["Distributors:EnableSampleSource"], out var on) && on;

        public Task<(bool Ok, string? Error)> TestConnection(DistributorCredentials credentials,
            CancellationToken ct = default)
        {
            // Accepts anything: there is nothing real to authenticate against. It still exercises
            // the round trip through decryption and the settings screen's button.
            return Task.FromResult((true, (string?)null));
        }

        public Task<DistributorCatalog> FetchCatalog(DistributorCredentials credentials,
            CancellationToken ct = default)
        {
            // The same columns a real distributor feed carries, and the same ones it doesn't:
            // no Price (the shop's own retail decision) and no stock (a catalog refresh is not a
            // stocktake). Syncing must leave both untouched, which is the thing worth watching.
            var present = new HashSet<string>
            {
                ShopImportColumn.Sku,
                ShopImportColumn.Barcode,
                ShopImportColumn.Mpn,
                ShopImportColumn.ManufacturerName,
                ShopImportColumn.VendorPartNumber,
                ShopImportColumn.Brand,
                ShopImportColumn.Cost,
                ShopImportColumn.Msrp,
            };

            return Task.FromResult(new DistributorCatalog(SampleProducts(), present));
        }

        /// <summary>
        /// Six variants across three products, including one product with two sizes so the
        /// group-into-variants path is covered. Every barcode is a REAL UPC-A with a correct mod-10
        /// check digit, because an invalid one is silently dropped by Gtin.Normalize and the scan
        /// half of the test would quietly prove nothing.
        /// </summary>
        public static List<ShopImportProduct> SampleProducts() => new()
        {
            new ShopImportProduct
            {
                Name = "Sample Tube 700x25",
                Brand = "Bontrager",
                CategoryName = "Tires & Tubes",
                SupplierName = "Sample Distributor (testing)",
                Variants = new()
                {
                    new ShopImportVariant
                    {
                        Sku = "SMP-TUBE-700", Barcode = "759677001024", Mpn = "BON-TU-700x25",
                        ManufacturerName = "Bontrager Standard Tube 700x25",
                        VendorPartNumber = "SMP-1001", CostCents = 420, MsrpCents = 999,
                        TrackingKind = "pool",
                    },
                },
            },
            new ShopImportProduct
            {
                Name = "Sample Chain Lube 4oz",
                Brand = "Finish Line",
                CategoryName = "Maintenance",
                SupplierName = "Sample Distributor (testing)",
                Variants = new()
                {
                    new ShopImportVariant
                    {
                        Sku = "SMP-LUBE-4", Barcode = "012345678905", Mpn = "FL-DRY-4OZ",
                        ManufacturerName = "Finish Line Dry Lube 4oz",
                        VendorPartNumber = "SMP-1002", CostCents = 610, MsrpCents = 1299,
                        TrackingKind = "pool",
                    },
                },
            },
            new ShopImportProduct
            {
                Name = "Sample Trail Jersey",
                Brand = "Fly Racing",
                CategoryName = "Apparel",
                SupplierName = "Sample Distributor (testing)",
                Variants = new()
                {
                    new ShopImportVariant
                    {
                        Sku = "SMP-JRS-M", Barcode = "888888000011", Mpn = "FR-JRS-24", Size = "M",
                        ManufacturerName = "Fly Racing Kinetic Jersey",
                        VendorPartNumber = "SMP-1003", CostCents = 1600, MsrpCents = 3999,
                        TrackingKind = "pool",
                    },
                    new ShopImportVariant
                    {
                        Sku = "SMP-JRS-L", Barcode = "765432109874", Mpn = "FR-JRS-24", Size = "L",
                        ManufacturerName = "Fly Racing Kinetic Jersey",
                        VendorPartNumber = "SMP-1004", CostCents = 1600, MsrpCents = 3999,
                        TrackingKind = "pool",
                    },
                    new ShopImportVariant
                    {
                        Sku = "SMP-JRS-XL", Barcode = "192837465005", Mpn = "FR-JRS-24", Size = "XL",
                        ManufacturerName = "Fly Racing Kinetic Jersey",
                        VendorPartNumber = "SMP-1005", CostCents = 1600, MsrpCents = 3999,
                        TrackingKind = "pool",
                    },
                },
            },
        };
    }
}
