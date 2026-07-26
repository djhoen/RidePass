using NUnit.Framework;
using Services.BikeShop;

namespace UnitTests
{
    // These tests protect TWO properties, for two different reasons:
    //
    //   1. A product name a tenant typed never reaches the cross-tenant parts library. That is a
    //      data-isolation guarantee.
    //   2. A manufacturer name obtained under a third party's per-dealer licence never reaches it
    //      either. That is a contractual one, and it is the subtler of the two: the string IS a
    //      genuine manufacturer name, so nothing about the value itself gives the danger away.
    //
    // The rule is a pure function so both can be pinned here, and this is what fails if someone
    // reintroduces a convenience fallback or adds a distributor to the allow-list without reading
    // its terms.
    [TestFixture]
    public class LibraryContributionTests
    {
        private const string Gtin = "00759677001024";

        [Test]
        public void ShopTypedName_IsContributed()
        {
            Assert.That(
                LibraryContribution.NameToContribute(Gtin, "Bontrager Standard Tube 700x25",
                    ManufacturerNameSource.Shop),
                Is.EqualTo("Bontrager Standard Tube 700x25"));
        }

        [Test]
        public void ImportedName_IsContributed()
        {
            // A CSV the shop uploaded: they chose to supply it and RidePass agreed to no terms over
            // a file a customer handed us.
            Assert.That(
                LibraryContribution.NameToContribute(Gtin, "Fly Racing Kinetic Jersey",
                    ManufacturerNameSource.Import),
                Is.EqualTo("Fly Racing Kinetic Jersey"));
        }

        [Test]
        public void QbpLicensedName_IsNeverContributed()
        {
            // THE case this guard exists for. QBP's Content Licensing Service is licensed per
            // dealer, so pooling one dealer's CLS content into a library other dealers read is
            // redistribution. Without this, switching on the nightly sync would have started doing
            // exactly that with no code change anywhere.
            Assert.That(
                LibraryContribution.NameToContribute(Gtin, "Bontrager Standard Tube 700x25",
                    ManufacturerNameSource.Qbp),
                Is.Null);
        }

        [Test]
        public void UnknownOrMissingSource_IsNeverContributed()
        {
            // An allow-list, not a deny-list: a distributor added later is un-poolable until
            // somebody reads its terms and adds it explicitly. That is the correct default for a
            // decision with legal consequences.
            Assert.That(LibraryContribution.NameToContribute(Gtin, "Some Part", null), Is.Null);
            Assert.That(LibraryContribution.NameToContribute(Gtin, "Some Part", ""), Is.Null);
            Assert.That(LibraryContribution.NameToContribute(Gtin, "Some Part", "jbi"), Is.Null);
            Assert.That(LibraryContribution.NameToContribute(Gtin, "Some Part", "hawley"), Is.Null);
        }

        [Test]
        public void NoManufacturerName_ContributesNothing()
        {
            // The tenant-privacy guard. A shop that never filled in the manufacturer field must
            // contribute NOTHING, rather than falling back to whatever they called the product.
            Assert.That(LibraryContribution.NameToContribute(Gtin, null, ManufacturerNameSource.Shop), Is.Null);
            Assert.That(LibraryContribution.NameToContribute(Gtin, "", ManufacturerNameSource.Shop), Is.Null);
            Assert.That(LibraryContribution.NameToContribute(Gtin, "   ", ManufacturerNameSource.Shop), Is.Null);
        }

        [Test]
        public void NoGtin_ContributesNothing()
        {
            // A shop's own SKU is not shareable identity: it means nothing to another shop, and
            // pooling on it would let one shop's private numbering collide with another's.
            Assert.That(LibraryContribution.NameToContribute(null, "Bontrager Tube", ManufacturerNameSource.Shop), Is.Null);
            Assert.That(LibraryContribution.NameToContribute("", "Bontrager Tube", ManufacturerNameSource.Shop), Is.Null);
            Assert.That(LibraryContribution.NameToContribute("   ", "Bontrager Tube", ManufacturerNameSource.Shop), Is.Null);
        }

        [Test]
        public void TrimsWhitespace()
        {
            Assert.That(
                LibraryContribution.NameToContribute(Gtin, "  Bontrager Tube  ", ManufacturerNameSource.Shop),
                Is.EqualTo("Bontrager Tube"));
        }

        [Test]
        public void PoolableSources_AreExactlyTheReviewedOnes()
        {
            // Pins the allow-list itself. Adding a source here should be a deliberate act with a
            // terms review behind it, so it has to break a test first.
            Assert.That(LibraryContribution.IsPoolableSource(ManufacturerNameSource.Shop), Is.True);
            Assert.That(LibraryContribution.IsPoolableSource(ManufacturerNameSource.Import), Is.True);
            Assert.That(LibraryContribution.IsPoolableSource(ManufacturerNameSource.Library), Is.True);
            Assert.That(LibraryContribution.IsPoolableSource(ManufacturerNameSource.Qbp), Is.False);
        }

        [Test]
        public void TheShopsOwnNameIsNeverAnInput()
        {
            // A structural guard rather than a behavioural one: the function's only name input is
            // the manufacturer's. If a future signature grows a productName parameter, this test
            // fails, which is the point at which someone has to justify it.
            var method = typeof(LibraryContribution).GetMethod(nameof(LibraryContribution.NameToContribute))!;
            var parameterNames = method.GetParameters().Select(p => p.Name).ToArray();
            Assert.That(parameterNames,
                Is.EqualTo(new[] { "gtin14", "manufacturerName", "manufacturerNameSource" }),
                "The contribution rule must only ever see manufacturer-sourced identity. A tenant's "
                + "own product name must not be reachable from here.");
        }
    }
}
