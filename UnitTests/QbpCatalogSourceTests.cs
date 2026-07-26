using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Services.BikeShop;
using Services.Distributors;
using Services.Repositories.Data.BikeShopData;

namespace UnitTests
{
    // The QBP transport can't be tested without a dealer account, but the MAPPING can, and the
    // mapping is where the decisions live: what a nightly sync is allowed to overwrite, and whether
    // licensed content can escape into the shared parts library. Both are pinned here.
    [TestFixture]
    public class QbpCatalogSourceTests
    {
        private static QbpCatalogSource Source() =>
            new(NullLogger<QbpCatalogSource>.Instance);

        private static List<QbpRow> TwoSizesOfOneJersey() => new()
        {
            new QbpRow
            {
                ProductName = "Fly Racing Kinetic Jersey", Brand = "Fly Racing", Category = "Apparel",
                QbpItemNumber = "QB-1001", Mpn = "FR-JRS-24", Upc = "759677001024", DealerSku = "JRS-M",
                Size = "M", DealerPrice = 16.00m, Msrp = 39.99m,
            },
            new QbpRow
            {
                ProductName = "Fly Racing Kinetic Jersey", Brand = "Fly Racing", Category = "Apparel",
                QbpItemNumber = "QB-1002", Mpn = "FR-JRS-24", Upc = "012345678905", DealerSku = "JRS-L",
                Size = "L", DealerPrice = 16.00m, Msrp = 39.99m,
            },
        };

        [Test]
        public void NamesFromThisFeedAreNeverPoolable()
        {
            // The whole reason provenance exists. QBP's Content Licensing Service is licensed per
            // dealer, so a name pulled under one shop's key must never reach a library other shops
            // read. If this ever returns a poolable source, the nightly sync silently starts
            // redistributing licensed content.
            var source = Source();
            Assert.That(source.ManufacturerNameSource, Is.EqualTo(ManufacturerNameSource.Qbp));
            Assert.That(LibraryContribution.IsPoolableSource(source.ManufacturerNameSource), Is.False);
        }

        [Test]
        public void PriceIsNotAColumnThisFeedWrites()
        {
            // A nightly job must never touch what a shop CHARGES. Cost and MSRP come from the
            // distributor; retail price is the shop's own decision. ImportCatalog writes only the
            // columns listed here, so this absence is the guarantee.
            var catalog = Source().MapToCatalog(TwoSizesOfOneJersey());
            Assert.That(catalog.PresentColumns, Does.Not.Contain(ShopImportColumn.Price));
            Assert.That(catalog.PresentColumns, Contains.Item(ShopImportColumn.Cost));
            Assert.That(catalog.PresentColumns, Contains.Item(ShopImportColumn.Msrp));
        }

        [Test]
        public void StockIsNotAColumnThisFeedWrites()
        {
            // Stock moves through the movement ledger so the count always has a story. A catalog
            // refresh is not a stocktake, and a distributor's on-hand is theirs, not the shop's.
            var catalog = Source().MapToCatalog(TwoSizesOfOneJersey());
            Assert.That(catalog.PresentColumns, Does.Not.Contain(ShopImportColumn.LowStock));
        }

        [Test]
        public void SizesOfOneJerseyBecomeVariantsOfOneProduct()
        {
            var catalog = Source().MapToCatalog(TwoSizesOfOneJersey());
            Assert.That(catalog.Products, Has.Count.EqualTo(1));
            Assert.That(catalog.Products[0].Variants, Has.Count.EqualTo(2));
            Assert.That(catalog.Products[0].Variants.Select(v => v.Size), Is.EquivalentTo(new[] { "M", "L" }));
        }

        [Test]
        public void ManufacturerNameIsSetOnEveryVariant()
        {
            // Without this the sync would fill a catalog but leave manufacturer_name null, so a
            // scan would still resolve nothing. It is the field the whole feature turns on.
            var catalog = Source().MapToCatalog(TwoSizesOfOneJersey());
            Assert.That(catalog.Products[0].Variants.Select(v => v.ManufacturerName),
                Is.All.EqualTo("Fly Racing Kinetic Jersey"));
        }

        [Test]
        public void DealerPriceBecomesCost_NotSalePrice()
        {
            var v = Source().MapToCatalog(TwoSizesOfOneJersey()).Products[0].Variants[0];
            Assert.That(v.CostCents, Is.EqualTo(1600));
            Assert.That(v.MsrpCents, Is.EqualTo(3999));
            Assert.That(v.SalePriceCents, Is.Null, "The shop sets its own retail price.");
        }

        [Test]
        public void QbpItemNumberBecomesTheVendorPartNumber()
        {
            // What a purchase order references, which is a different identifier from the MPN.
            var v = Source().MapToCatalog(TwoSizesOfOneJersey()).Products[0].Variants[0];
            Assert.That(v.VendorPartNumber, Is.EqualTo("QB-1001"));
            Assert.That(v.Mpn, Is.EqualTo("FR-JRS-24"));
        }

        [Test]
        public void RowsWithNoProductNameAreDropped()
        {
            // A nameless row cannot become a product, and letting it through would create a row
            // named "" that a shop then has to find and delete.
            var rows = TwoSizesOfOneJersey();
            rows.Add(new QbpRow { ProductName = null, Upc = "999999999999" });
            rows.Add(new QbpRow { ProductName = "   ", Upc = "888888888888" });
            var catalog = Source().MapToCatalog(rows);
            Assert.That(catalog.Products, Has.Count.EqualTo(1));
        }

        [Test]
        public void NotConfiguredUntilTheTransportIsWritten()
        {
            // Guards against a half-finished vendor client being switched on: while this is false
            // the sweep skips rather than failing every shop's sync nightly with an error they
            // cannot act on.
            Assert.That(Source().IsConfigured, Is.False);
        }

        [Test]
        public void FetchThrowsAClearMessageRatherThanANullReference()
        {
            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                () => Source().FetchCatalog(new DistributorCredentials(null, null, null, null)));
            Assert.That(ex!.Message, Does.Contain("Import"),
                "The message should point at the workaround that does work today.");
        }
    }
}
