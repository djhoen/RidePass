using NUnit.Framework;
using Services.Payments;

namespace UnitTests
{
    // ServiceChargeSplit is the single place the platform charge is split between what the
    // customer is billed and what the tenant absorbs. Rentals, counter sales, the online store
    // and work-order bill-outs all go through it, so a change here moves real money on four
    // surfaces at once. These tests pin the invariants that keep those four in agreement.
    [TestFixture]
    public class ServiceChargeSplitTests
    {
        [Test]
        public void CustomerPaysAll_ChargesTheFullFee()
        {
            var r = ServiceChargeSplit.Compute(baseCents: 10000, serviceChargeBps: 300, customerPaidBps: 10000);
            Assert.That(r.ServiceChargeCents, Is.EqualTo(300));
            Assert.That(r.CustomerPaidCents, Is.EqualTo(300));
        }

        [Test]
        public void TenantAbsorbs_StillOwesTheFee_ButChargesTheCustomerNothing()
        {
            // The distinction the whole feature rests on: absorbing changes WHO funds the charge,
            // never WHETHER it is owed. A zero here would mean RidePass earns nothing on the sale.
            var r = ServiceChargeSplit.Compute(baseCents: 10000, serviceChargeBps: 300, customerPaidBps: 0);
            Assert.That(r.ServiceChargeCents, Is.EqualTo(300));
            Assert.That(r.CustomerPaidCents, Is.EqualTo(0));
        }

        [Test]
        public void SplitFunding_FloorsAtEachStep()
        {
            // 12345 * 3% = 370.35 -> 370; 370 * 50% = 185. Floor twice, matching the frontend preview.
            var r = ServiceChargeSplit.Compute(baseCents: 12345, serviceChargeBps: 300, customerPaidBps: 5000);
            Assert.That(r.ServiceChargeCents, Is.EqualTo(370));
            Assert.That(r.CustomerPaidCents, Is.EqualTo(185));
        }

        [Test]
        public void CustomerShareNeverExceedsTheChargeItself()
        {
            var r = ServiceChargeSplit.Compute(baseCents: 9999, serviceChargeBps: 275, customerPaidBps: 10000);
            Assert.That(r.CustomerPaidCents, Is.LessThanOrEqualTo(r.ServiceChargeCents));
        }

        [Test]
        public void ZeroRate_ChargesNothing()
        {
            var r = ServiceChargeSplit.Compute(baseCents: 50000, serviceChargeBps: 0, customerPaidBps: 10000);
            Assert.That(r.ServiceChargeCents, Is.EqualTo(0));
            Assert.That(r.CustomerPaidCents, Is.EqualTo(0));
        }

        [Test]
        public void NegativeBase_ClampsToZero_RatherThanRefundingAFee()
        {
            // A fully discounted sale can land at or below zero. A negative fee would credit the
            // tenant for a charge nobody paid, so the base clamps instead.
            var r = ServiceChargeSplit.Compute(baseCents: -500, serviceChargeBps: 300, customerPaidBps: 10000);
            Assert.That(r.ServiceChargeCents, Is.EqualTo(0));
            Assert.That(r.CustomerPaidCents, Is.EqualTo(0));
        }

        [Test]
        public void NegativeBps_ClampToZero()
        {
            var r = ServiceChargeSplit.Compute(baseCents: 10000, serviceChargeBps: -300, customerPaidBps: -1);
            Assert.That(r.ServiceChargeCents, Is.EqualTo(0));
            Assert.That(r.CustomerPaidCents, Is.EqualTo(0));
        }

        [Test]
        public void LargeSale_DoesNotOverflow()
        {
            // 100k dollars at 3%: the intermediate is baseCents * bps, which overflows int32 well
            // before this, so the implementation widens to long. This test is the guard on that.
            var r = ServiceChargeSplit.Compute(baseCents: 10_000_000, serviceChargeBps: 300, customerPaidBps: 10000);
            Assert.That(r.ServiceChargeCents, Is.EqualTo(300_000));
            Assert.That(r.CustomerPaidCents, Is.EqualTo(300_000));
        }

        [Test]
        public void MatchesRentalCharge_OnTheSameInputs()
        {
            // RentalCharge delegates its split here. If the two ever disagree, a rental booked at
            // the counter and the same rental bought online would bill different fees.
            var split = ServiceChargeSplit.Compute(baseCents: 20000, serviceChargeBps: 300, customerPaidBps: 5000);
            var rental = RentalCharge.Compute(
                subtotalAfterDiscountCents: 20000, serviceChargeBps: 300,
                riderPaidServiceChargeBps: 5000, depositPerUnitCents: 0, quantity: 1);
            Assert.That(rental.ServiceChargeCents, Is.EqualTo(split.ServiceChargeCents));
            Assert.That(rental.RiderServiceChargeCents, Is.EqualTo(split.CustomerPaidCents));
        }
    }
}
