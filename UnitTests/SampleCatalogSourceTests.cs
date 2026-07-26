using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Services.BikeShop;
using Services.Distributors;
using Services.Repositories.Data.BikeShopData;

namespace UnitTests
{
    // The sample distributor is a test fixture that ships in the product, so the things worth
    // pinning are the ones that keep it from doing harm: it must be OFF unless explicitly switched
    // on, and its data must not be poolable into the shared library.
    [TestFixture]
    public class SampleCatalogSourceTests
    {
        // A hand-rolled IConfiguration rather than ConfigurationBuilder: Services references only
        // Configuration.Abstractions, and adding the in-memory provider package to the solution for
        // one test's benefit is a worse trade than fifteen lines here. Only the indexer is used.
        private class FakeConfig : IConfiguration
        {
            private readonly Dictionary<string, string?> _values;
            public FakeConfig(Dictionary<string, string?> values) => _values = values;

            public string? this[string key]
            {
                get => _values.TryGetValue(key, out var v) ? v : null;
                set => _values[key] = value;
            }

            public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();
            public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() =>
                throw new NotSupportedException();
            public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
        }

        private static SampleCatalogSource With(params (string Key, string Value)[] settings) =>
            new(new FakeConfig(settings.ToDictionary(s => s.Key, s => (string?)s.Value)));

        [Test]
        public void OffWhenTheSettingIsAbsent()
        {
            // Production never sets this key, so this is the case that keeps a fake distributor off
            // a real shop's settings screen.
            Assert.That(With().IsConfigured, Is.False);
        }

        [Test]
        public void OffForAnythingThatIsNotTrue()
        {
            Assert.That(With(("Distributors:EnableSampleSource", "false")).IsConfigured, Is.False);
            Assert.That(With(("Distributors:EnableSampleSource", "")).IsConfigured, Is.False);
            Assert.That(With(("Distributors:EnableSampleSource", "yes")).IsConfigured, Is.False);
            Assert.That(With(("Distributors:EnableSampleSource", "1")).IsConfigured, Is.False);
        }

        [Test]
        public void OnOnlyWhenExplicitlyEnabled()
        {
            Assert.That(With(("Distributors:EnableSampleSource", "true")).IsConfigured, Is.True);
            Assert.That(With(("Distributors:EnableSampleSource", "True")).IsConfigured, Is.True);
        }

        [Test]
        public void ItsNamesAreNotPoolable()
        {
            // A test run must demonstrate the licensing guard, not bypass it. If 'sample' ever
            // became poolable, syncing the fixture would push invented manufacturer names into the
            // library that real shops read.
            Assert.That(LibraryContribution.IsPoolableSource(ManufacturerNameSource.Sample), Is.False);
            Assert.That(With().ManufacturerNameSource, Is.EqualTo(ManufacturerNameSource.Sample));
        }

        [Test]
        public void EveryBarcodeIsAValidGtin()
        {
            // The point of syncing the fixture is to then SCAN one of these at the register. An
            // invalid check digit is silently dropped by Gtin.Normalize, so the scan half of the
            // test would pass by doing nothing. This is what stops that.
            var barcodes = SampleCatalogSource.SampleProducts()
                .SelectMany(p => p.Variants)
                .Select(v => v.Barcode)
                .ToList();

            Assert.That(barcodes, Is.All.Not.Null);
            foreach (var barcode in barcodes)
            {
                Assert.That(Gtin.Normalize(barcode), Is.Not.Null,
                    $"Sample barcode {barcode} has a bad check digit, so a scan of it would never resolve.");
            }
        }

        [Test]
        public void BarcodesAreUnique()
        {
            // Two variants sharing a barcode would collide on the unique index mid-sync and leave a
            // half-written catalog, which would look like a bug in the sync rather than the fixture.
            var barcodes = SampleCatalogSource.SampleProducts().SelectMany(p => p.Variants)
                .Select(v => Gtin.Normalize(v.Barcode)).ToList();
            Assert.That(barcodes.Distinct().Count(), Is.EqualTo(barcodes.Count));
        }

        [Test]
        public void CarriesNoPriceOrStockColumns()
        {
            // The fixture has to model a real feed faithfully or testing it proves the wrong thing:
            // a sync must never touch what a shop charges or what it has on hand.
            var catalog = With(("Distributors:EnableSampleSource", "true"))
                .FetchCatalog(new DistributorCredentials(null, null, null, null)).Result;

            Assert.That(catalog.PresentColumns, Does.Not.Contain(ShopImportColumn.Price));
            Assert.That(catalog.PresentColumns, Does.Not.Contain(ShopImportColumn.LowStock));
            Assert.That(catalog.PresentColumns, Contains.Item(ShopImportColumn.Cost));
        }

        [Test]
        public void HasAMultiVariantProduct()
        {
            // Covers the grouping path: sizes of one jersey must arrive as variants of one product
            // rather than three separate products.
            var products = SampleCatalogSource.SampleProducts();
            Assert.That(products.Any(p => p.Variants.Count > 1), Is.True);
            Assert.That(products.SelectMany(p => p.Variants).All(v => !string.IsNullOrWhiteSpace(v.ManufacturerName)),
                Is.True, "Every sample variant needs a manufacturer name or the scan test proves nothing.");
        }
    }
}
