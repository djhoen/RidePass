using NUnit.Framework;
using Services.Payments;

namespace UnitTests
{
    // Tax on the buyer-paid platform fee of a shop sale. Split out of the three controllers that
    // create a shop_sale so the counter, the online store and a work-order bill-out cannot charge
    // different tax on the same fee — a discrepancy that surfaces in an audit rather than a bug
    // report.
    //
    // Existed because retail silently never taxed the fee while rentals made it a tenant choice
    // (tenant.rental_tax_service_charge_taxable, Script0214). Two sibling paths, opposite treatment.
    [TestFixture]
    public class ShopFeeTaxTests
    {
        [Test]
        public void TaxesTheFeeAtTheDefaultCategoryRate()
        {
            // $3.00 fee at 8.25%: 24.75c -> 25c.
            Assert.That(ShopFeeTax.Compute(300, taxable: true, 825, pricesIncludeTax: false), Is.EqualTo(25));
        }

        [Test]
        public void OptedOut_ChargesNoTaxOnTheFee()
        {
            // The whole point of the setting: a jurisdiction that doesn't tax service fees.
            Assert.That(ShopFeeTax.Compute(300, taxable: false, 825, pricesIncludeTax: false), Is.Zero);
        }

        [Test]
        public void NoDefaultTaxCategory_ChargesNothing()
        {
            // Consistent with how an uncategorised PRODUCT already behaves at this shop: untaxed
            // rather than guessed at. A tenant with no default category is a real case (verified on
            // dev), so this must not throw or invent a rate.
            Assert.That(ShopFeeTax.Compute(300, taxable: true, null, pricesIncludeTax: false), Is.Zero);
            Assert.That(ShopFeeTax.Compute(300, taxable: true, 0, pricesIncludeTax: false), Is.Zero);
        }

        [Test]
        public void NoFee_ChargesNothing()
        {
            // The default configuration for every tenant: the shop absorbs the charge, so the
            // customer pays no fee and there is nothing to tax. This is why defaulting the new
            // setting to true changed no existing total.
            Assert.That(ShopFeeTax.Compute(0, taxable: true, 825, pricesIncludeTax: false), Is.Zero);
        }

        [Test]
        public void AbsorbedFeeIsNeverTaxedToTheCustomer()
        {
            // Only the CUSTOMER's share can be taxed to them. A fee the shop absorbs never reaches
            // their total, so taxing it would charge them tax on money they didn't pay.
            Assert.That(ShopFeeTax.Compute(-50, taxable: true, 825, pricesIncludeTax: false), Is.Zero);
        }

        [Test]
        public void TaxInclusivePricing_ExtractsRatherThanAdds()
        {
            // A tax-inclusive shop must not have tax ADDED to the fee while every line on the same
            // receipt has it extracted; the totals would not reconcile.
            var extracted = ShopFeeTax.Compute(300, taxable: true, 825, pricesIncludeTax: true);
            var added = ShopFeeTax.Compute(300, taxable: true, 825, pricesIncludeTax: false);
            Assert.That(extracted, Is.LessThan(added));
            // 300 - round(300 * 10000 / 10825) = 300 - 277 = 23
            Assert.That(extracted, Is.EqualTo(23));
        }

        [Test]
        public void MatchesTheLineTaxFormula()
        {
            // The fee is taxed exactly like another line on the sale. Mirrors the controllers'
            // ComputeLineTax so a fee and a product of the same value at the same rate agree.
            static int LineTax(int baseCents, int rateBps, bool inclusive) =>
                inclusive
                    ? baseCents - (int)Math.Round(baseCents * 10000.0 / (10000.0 + rateBps), MidpointRounding.AwayFromZero)
                    : (int)Math.Round(baseCents * rateBps / 10000.0, MidpointRounding.AwayFromZero);

            foreach (var amount in new[] { 1, 7, 99, 300, 1234, 99999 })
            {
                foreach (var rate in new[] { 1, 500, 825, 1000, 1375 })
                {
                    Assert.That(ShopFeeTax.Compute(amount, true, rate, false),
                        Is.EqualTo(LineTax(amount, rate, false)), $"exclusive {amount}@{rate}");
                    Assert.That(ShopFeeTax.Compute(amount, true, rate, true),
                        Is.EqualTo(LineTax(amount, rate, true)), $"inclusive {amount}@{rate}");
                }
            }
        }
    }
}
