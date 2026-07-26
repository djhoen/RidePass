using NUnit.Framework;
using Services.BikeShop;
using Services.Distributors;

namespace UnitTests
{
    // Regression cover for a bug found by running a real sync against a real database: a size run
    // sharing one manufacturer part number collapsed into a single variant.
    //
    // ImportCatalog matches an incoming row to an existing variant by GTIN, then MPN, then SKU. The
    // fall-through was unconditional, so a jersey in M/L/XL (one MPN, three barcodes) imported as
    // ONE variant: the first size was created, and every later size matched it on MPN and
    // overwrote it. A shop silently ended up with a third of their apparel, and a nightly
    // distributor sync would have redone the damage every night.
    //
    // The matching itself lives in a local function inside a repository method and needs a database
    // to exercise, so these tests pin the two things that CAN be checked in isolation: the fixture
    // that reproduces the shape, and the invariant that makes the shape safe.
    [TestFixture]
    public class ImportMatchingTests
    {
        [Test]
        public void SampleFixtureStillReproducesTheSharedMpnShape()
        {
            // If this stops being true the end-to-end harness stops covering the bug, and the
            // regression could return unnoticed. The fixture earns its keep by having a size run.
            var variants = SampleCatalogSource.SampleProducts().SelectMany(p => p.Variants).ToList();

            var sharedMpn = variants
                .Where(v => !string.IsNullOrWhiteSpace(v.Mpn))
                .GroupBy(v => v.Mpn!)
                .Where(g => g.Count() > 1)
                .ToList();

            Assert.That(sharedMpn, Is.Not.Empty,
                "The sample catalog must keep at least one MPN shared across sizes, or it no longer "
                + "reproduces the collapse bug it exists to guard.");
            Assert.That(sharedMpn[0].Count(), Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void RowsSharingAnMpnStillCarryDistinctBarcodes()
        {
            // The premise the fix rests on: a differing barcode is positive evidence of a different
            // item. Rows that share an MPN must therefore differ by GTIN, or nothing can tell them
            // apart and the collapse would be correct behaviour rather than a bug.
            var bySharedMpn = SampleCatalogSource.SampleProducts()
                .SelectMany(p => p.Variants)
                .Where(v => !string.IsNullOrWhiteSpace(v.Mpn))
                .GroupBy(v => v.Mpn!)
                .Where(g => g.Count() > 1);

            foreach (var group in bySharedMpn)
            {
                var gtins = group.Select(v => Gtin.Normalize(v.Barcode)).ToList();
                Assert.That(gtins, Is.All.Not.Null, $"MPN {group.Key} has a row with no valid barcode.");
                Assert.That(gtins.Distinct().Count(), Is.EqualTo(gtins.Count),
                    $"MPN {group.Key} has rows sharing a barcode, so they are genuinely indistinguishable.");
            }
        }

        [Test]
        public void SharedMpnRowsAreDistinguishedBySize()
        {
            // What made the collapse invisible in the data: the rows differ only by an attribute
            // the matcher never looked at.
            var jersey = SampleCatalogSource.SampleProducts()
                .SelectMany(p => p.Variants)
                .Where(v => v.Mpn == "FR-JRS-24")
                .ToList();

            Assert.That(jersey, Has.Count.EqualTo(3));
            Assert.That(jersey.Select(v => v.Size), Is.EquivalentTo(new[] { "M", "L", "XL" }));
        }
    }
}
