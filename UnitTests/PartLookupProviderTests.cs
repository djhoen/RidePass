using NUnit.Framework;
using Services.BikeShop;

namespace UnitTests
{
    // The shared parts library ships with NO external lookup vendor wired up, and that is a
    // licensing decision rather than an oversight (see IPartLookupProvider). These tests pin the
    // disabled default so a future change that registers a vendor has to be deliberate: if someone
    // swaps the DI registration without thinking about the vendor's terms, this is what fails.
    [TestFixture]
    public class PartLookupProviderTests
    {
        [Test]
        public void Default_IsDisabled()
        {
            var provider = new DisabledPartLookupProvider();
            Assert.That(provider.IsEnabled, Is.False,
                "The default provider must stay disabled: enabling an external vendor means caching "
                + "their product data, which Go-UPC's terms bar redistributing.");
        }

        [Test]
        public async Task Disabled_LookupAlwaysMisses()
        {
            var provider = new DisabledPartLookupProvider();
            Assert.That(await provider.Lookup("00759677001024"), Is.Null);
        }

        [Test]
        public void Disabled_NeverClaimsAVendorSlug()
        {
            // PurgeSource refuses to delete 'tenant_confirmed' and 'staff'. If the disabled
            // provider ever adopted one of those slugs, rows it wrote could not be purged.
            var provider = new DisabledPartLookupProvider();
            Assert.That(provider.SourceSlug, Is.Not.EqualTo("tenant_confirmed"));
            Assert.That(provider.SourceSlug, Is.Not.EqualTo("staff"));
        }
    }
}
