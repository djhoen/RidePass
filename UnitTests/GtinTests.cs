using NUnit.Framework;
using Services.BikeShop;

namespace UnitTests
{
    // The point of Gtin is that the same physical part scans to the same key however it was
    // printed. These pin that, and pin the refusals just as hard: a helper that "helpfully"
    // normalises a shop's own SKU into a GTIN would sell the customer the wrong part.
    [TestFixture]
    public class GtinTests
    {
        // The same notional item across widths. Check digit 4 is the real one for this prefix
        // (weights from the right: 3,1,3,1... sum 96, so the check digit is what takes it to 100).
        const string UpcA = "759677001024";        // 12
        const string Ean13 = "0759677001024";      // 13, the same code with the leading zero
        const string Gtin14 = "00759677001024";    // 14, padded again

        [Test]
        public void EveryWidthOfTheSameCode_NormalisesToOneKey()
        {
            // This is the bug that prompted the work: a part stored as UPC-A did not match the
            // same part scanned as a 13-digit EAN, because the comparison was string equality.
            var expected = Gtin.Normalize(UpcA);
            Assert.That(expected, Is.EqualTo(Gtin14));
            Assert.That(Gtin.Normalize(Ean13), Is.EqualTo(expected));
            Assert.That(Gtin.Normalize(Gtin14), Is.EqualTo(expected));
        }

        [Test]
        public void SeparatorsAndWhitespace_AreIgnored()
        {
            // How a human retypes a code off the label, hyphens and all.
            Assert.That(Gtin.Normalize("  759677001024 "), Is.EqualTo(Gtin14));
            Assert.That(Gtin.Normalize("7-596770-01024"), Is.EqualTo(Gtin14));
        }

        [Test]
        public void AnEan8_IsAccepted()
        {
            // Small items (a tube patch kit) carry EAN-8. 96385074 is the canonical valid example.
            Assert.That(Gtin.Normalize("96385074"), Is.EqualTo("00000096385074"));
        }

        [Test]
        public void ABadCheckDigit_IsRejected()
        {
            // A mistyped digit must fail rather than resolve to a neighbouring product.
            Assert.That(Gtin.Normalize("759677001025"), Is.Null);
            Assert.That(Gtin.IsValid("759677001025"), Is.False);
        }

        [Test]
        public void AShopSku_IsNotAGtin()
        {
            // The caller falls through to a SKU match on null. If these returned a value, scanning
            // a shop's own label would match against the barcode column and could hit the wrong row.
            Assert.That(Gtin.Normalize("BIKE-250F"), Is.Null);
            Assert.That(Gtin.Normalize("HELM-RNT"), Is.Null);
            Assert.That(Gtin.Normalize("ABC123"), Is.Null);
        }

        [Test]
        public void WrongLengthDigitStrings_AreRejected()
        {
            // 9, 10 and 11 digits are not GS1 widths; accepting them would invent a product.
            Assert.That(Gtin.Normalize("1234567890"), Is.Null);
            Assert.That(Gtin.Normalize("123456789012345"), Is.Null, "15 digits is past GTIN-14");
        }

        [Test]
        public void EmptyAndNull_AreRejected()
        {
            Assert.That(Gtin.Normalize(null), Is.Null);
            Assert.That(Gtin.Normalize(""), Is.Null);
            Assert.That(Gtin.Normalize("   "), Is.Null);
            Assert.That(Gtin.Normalize("----"), Is.Null, "separators alone are not a code");
        }

        [Test]
        public void NormalisingIsIdempotent()
        {
            // The column stores the normalised form, so re-normalising a stored value on the way
            // back through a match must not change it.
            var once = Gtin.Normalize(UpcA)!;
            Assert.That(Gtin.Normalize(once), Is.EqualTo(once));
        }
    }
}
