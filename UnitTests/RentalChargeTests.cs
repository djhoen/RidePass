using NUnit.Framework;
using Services.Payments;

namespace UnitTests
{
    // Pins the one invariant that matters for rental deposits: the RidePass service charge is
    // computed on the rental subtotal ONLY. A security deposit is refundable money we hold, not
    // revenue, so we never take a cut of it. These tests exist so a future edit to the rental
    // pricing can't quietly fold the deposit into the fee base.
    [TestFixture]
    public class RentalChargeTests
    {
        [Test]
        public void ServiceCharge_IsOnTheSubtotalOnly_NotTheDeposit()
        {
            // $100/day * 2 days = $200 subtotal, 3% service charge, rider pays 100%, $150 deposit.
            var c = RentalCharge.Compute(
                subtotalAfterDiscountCents: 20000, serviceChargeBps: 300,
                riderPaidServiceChargeBps: 10000, depositPerUnitCents: 15000, quantity: 1);

            Assert.That(c.ServiceChargeCents, Is.EqualTo(600), "3% of the $200 subtotal, deposit excluded");
            Assert.That(c.RiderServiceChargeCents, Is.EqualTo(600), "rider pays 100% of it");
            Assert.That(c.RentalAmountCents, Is.EqualTo(20600), "subtotal + rider service charge, no deposit");
            Assert.That(c.DepositCents, Is.EqualTo(15000));
            Assert.That(c.GrossCents, Is.EqualTo(20600 + 15000), "the card is charged rental + deposit");
        }

        [Test]
        public void DepositSize_NeverChangesTheFee()
        {
            // The regression guard: whatever the deposit, the service charge is identical. A deposit
            // that leaked into the base would make these two differ.
            var small = RentalCharge.Compute(20000, 300, 10000, 0, 1);
            var huge  = RentalCharge.Compute(20000, 300, 10000, 5_000_00, 1);

            Assert.That(huge.ServiceChargeCents, Is.EqualTo(small.ServiceChargeCents));
            Assert.That(huge.RiderServiceChargeCents, Is.EqualTo(small.RiderServiceChargeCents));
            Assert.That(huge.RentalAmountCents, Is.EqualTo(small.RentalAmountCents),
                "the amount our cut is computed against is deposit-free");
        }

        [Test]
        public void DepositIsPerUnit_TimesQuantity_AndStillFeeFree()
        {
            // 3 units, $150 deposit each. The deposit scales with quantity; the fee still doesn't see it.
            var c = RentalCharge.Compute(
                subtotalAfterDiscountCents: 60000, serviceChargeBps: 300,
                riderPaidServiceChargeBps: 10000, depositPerUnitCents: 15000, quantity: 3);

            Assert.That(c.DepositCents, Is.EqualTo(45000), "150 * 3");
            Assert.That(c.ServiceChargeCents, Is.EqualTo(1800), "3% of the $600 subtotal");
            Assert.That(c.GrossCents, Is.EqualTo(61800 + 45000));
        }

        [Test]
        public void RiderPaidBps_SplitsTheServiceCharge_WithoutTouchingTheDeposit()
        {
            // Track eats half the service charge (rider pays 50%). The deposit is untouched either way.
            var c = RentalCharge.Compute(
                subtotalAfterDiscountCents: 20000, serviceChargeBps: 300,
                riderPaidServiceChargeBps: 5000, depositPerUnitCents: 15000, quantity: 1);

            Assert.That(c.ServiceChargeCents, Is.EqualTo(600), "full charge is still 3% of subtotal");
            Assert.That(c.RiderServiceChargeCents, Is.EqualTo(300), "rider pays half");
            Assert.That(c.RentalAmountCents, Is.EqualTo(20300), "subtotal + the rider's half only");
            Assert.That(c.DepositCents, Is.EqualTo(15000), "deposit unaffected by the split");
        }
    }
}
