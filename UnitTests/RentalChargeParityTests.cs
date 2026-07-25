using NUnit.Framework;
using Services.Payments;

namespace UnitTests
{
    // RentalChargeTests pins the invariant. This pins the REFACTOR: BikeShopRentalController.Book
    // used to compute the fee inline, and now delegates to RentalCharge.ForTotalDeposit. These
    // cases replay the exact integer expressions the controller used to run, so a divergence in
    // rounding or flooring shows up here rather than as cents of drift on a real booking.
    [TestFixture]
    public class RentalChargeParityTests
    {
        // The two lines that used to live in the controller, verbatim.
        private static (int ServiceCharge, int RiderFee) Inline(int subtotal, int serviceChargeBps, int riderPaidBps)
        {
            var serviceChargeCents = (int)((long)subtotal * serviceChargeBps / 10_000L);
            var renterFeeCents = (int)((long)serviceChargeCents * riderPaidBps / 10_000L);
            return (serviceChargeCents, renterFeeCents);
        }

        [Test]
        [TestCase(0, 300, 10000)]
        [TestCase(1, 300, 10000)]
        [TestCase(4999, 250, 10000)]      // floors to a non-round cent both times
        [TestCase(20000, 300, 10000)]
        [TestCase(20000, 300, 5000)]
        [TestCase(20000, 300, 0)]         // track absorbs the whole fee
        [TestCase(33333, 175, 3333)]      // deliberately awkward: floor, then floor again
        [TestCase(100, 1, 1)]             // sub-cent charge truncates to zero, as it always did
        [TestCase(int.MaxValue / 2, 300, 10000)]
        public void MatchesTheInlineMathItReplaced(int subtotal, int serviceChargeBps, int riderPaidBps)
        {
            var expected = Inline(subtotal, serviceChargeBps, riderPaidBps);
            var actual = RentalCharge.ForTotalDeposit(subtotal, serviceChargeBps, riderPaidBps, totalDepositCents: 0);

            Assert.That(actual.ServiceChargeCents, Is.EqualTo(expected.ServiceCharge));
            Assert.That(actual.RiderServiceChargeCents, Is.EqualTo(expected.RiderFee));
            Assert.That(actual.RentalAmountCents, Is.EqualTo(subtotal + expected.RiderFee));
        }

        [Test]
        public void TotalDepositIsCarriedThrough_AndStillOutOfTheFeeBase()
        {
            // The shop rental path sums the deposit across lines with different per-unit deposits,
            // so it has no (deposit, quantity) pair. Whatever that total is, the fee is unmoved.
            var noDeposit = RentalCharge.ForTotalDeposit(20000, 300, 10000, 0);
            var bigDeposit = RentalCharge.ForTotalDeposit(20000, 300, 10000, 250_000);

            Assert.That(bigDeposit.ServiceChargeCents, Is.EqualTo(noDeposit.ServiceChargeCents));
            Assert.That(bigDeposit.RiderServiceChargeCents, Is.EqualTo(noDeposit.RiderServiceChargeCents));
            Assert.That(bigDeposit.RentalAmountCents, Is.EqualTo(noDeposit.RentalAmountCents));
            Assert.That(bigDeposit.DepositCents, Is.EqualTo(250_000));
        }

        [Test]
        public void PerUnitOverload_AgreesWithTheTotalledOne()
        {
            var perUnit = RentalCharge.Compute(60000, 300, 10000, depositPerUnitCents: 15000, quantity: 3);
            var totalled = RentalCharge.ForTotalDeposit(60000, 300, 10000, totalDepositCents: 45000);

            Assert.That(perUnit, Is.EqualTo(totalled));
        }

        [Test]
        public void ADiscountBiggerThanTheRental_NeverProducesANegativeFee()
        {
            // A benefit discount is capped at the rental amount upstream, but a negative base here
            // would mean issuing a service-charge credit, which is not a thing we do.
            var c = RentalCharge.ForTotalDeposit(-5000, 300, 10000, 15000);

            Assert.That(c.ServiceChargeCents, Is.EqualTo(0));
            Assert.That(c.RiderServiceChargeCents, Is.EqualTo(0));
            Assert.That(c.RentalAmountCents, Is.EqualTo(0));
            Assert.That(c.DepositCents, Is.EqualTo(15000), "the deposit is still owed");
        }
    }
}
