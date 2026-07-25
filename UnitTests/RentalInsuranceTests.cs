using NUnit.Framework;
using Services.Payments;

namespace UnitTests
{
    // The damage waiver ("insurance") has three rules that are easy to get wrong in one path and
    // right in the other two, because three separate controllers book rentals: the counter, the
    // customer storefront, and packages. These pin all three so the paths cannot diverge.
    //
    //   1. The fee is a percentage of the GROSS rental, before any season-pass discount.
    //   2. Taking it WAIVES the deposit entirely.
    //   3. It rides inside the subtotal, so the service fee and tax apply to it.
    [TestFixture]
    public class RentalInsuranceTests
    {
        const int Rate = 1500;          // 15%
        const int ServiceBps = 300;     // 3%
        const int RiderPaysAll = 10000;

        [Test]
        public void NotTakenOrNotOffered_IsFree()
        {
            Assert.That(RentalCharge.InsuranceFor(20000, Rate, offered: true, taken: false), Is.EqualTo(0));
            Assert.That(RentalCharge.InsuranceFor(20000, Rate, offered: false, taken: true), Is.EqualTo(0),
                "a renter cannot buy what the track does not offer");
            Assert.That(RentalCharge.InsuranceFor(20000, rateBps: 0, offered: true, taken: true), Is.EqualTo(0),
                "offered at a zero rate is not a charge");
        }

        [Test]
        public void FeeIsOnTheGrossRental_NotTheDiscountedOne()
        {
            // $200 gear, rider holds a pass worth $50 off. The waiver still prices $200 of gear:
            // the risk of it going out the door does not shrink because they hold a pass.
            var fee = RentalCharge.InsuranceFor(grossRentalCents: 20000, Rate, offered: true, taken: true);
            Assert.That(fee, Is.EqualTo(3000), "15% of the $200 GROSS, not of the $150 net");
        }

        [Test]
        public void TakingIt_WaivesTheDeposit()
        {
            var withWaiver = RentalCharge.WithInsurance(
                netRentalCents: 20000, insuranceCents: 3000, ServiceBps, RiderPaysAll, totalDepositCents: 50000);

            Assert.That(withWaiver.DepositCents, Is.EqualTo(0), "that is what the renter paid for");
            Assert.That(withWaiver.GrossCents, Is.EqualTo(withWaiver.RentalAmountCents),
                "nothing is held, so the card sees only the rental");
        }

        [Test]
        public void DecliningIt_KeepsTheDeposit()
        {
            var noWaiver = RentalCharge.WithInsurance(
                netRentalCents: 20000, insuranceCents: 0, ServiceBps, RiderPaysAll, totalDepositCents: 50000);

            Assert.That(noWaiver.DepositCents, Is.EqualTo(50000));
            Assert.That(noWaiver.ServiceChargeCents, Is.EqualTo(600), "3% of $200, deposit still excluded");
        }

        [Test]
        public void TheFeeIsInsideTheServiceChargeBase()
        {
            // $200 rental + $30 waiver = $230 subtotal; 3% of that is $6.90, not the $6.00 the
            // rental alone would attract. The waiver is revenue for a service, so it is fee'd.
            var c = RentalCharge.WithInsurance(
                netRentalCents: 20000, insuranceCents: 3000, ServiceBps, RiderPaysAll, totalDepositCents: 50000);

            Assert.That(c.ServiceChargeCents, Is.EqualTo(690));
            Assert.That(c.RentalAmountCents, Is.EqualTo(23000 + 690));
        }

        [Test]
        public void TheWaiverNeverPaysTheDepositAndTheDepositNeverPaysTheFee()
        {
            // The cross-check: a huge deposit must not move the waiver's fee base, and the waiver
            // must not resurrect a deposit. One assertion for each direction.
            var smallDeposit = RentalCharge.WithInsurance(20000, 3000, ServiceBps, RiderPaysAll, 100);
            var hugeDeposit = RentalCharge.WithInsurance(20000, 3000, ServiceBps, RiderPaysAll, 5_000_00);

            Assert.That(hugeDeposit.ServiceChargeCents, Is.EqualTo(smallDeposit.ServiceChargeCents));
            Assert.That(hugeDeposit.DepositCents, Is.EqualTo(0));
            Assert.That(smallDeposit.DepositCents, Is.EqualTo(0));
        }

        [Test]
        public void MatchesTheInlineMathTheStorefrontUsed()
        {
            // Parity with ShopStoreController's original expressions, so moving it here changed
            // nothing a renter would be charged.
            const int amount = 20000, benefitDiscount = 5000, depositTotal = 50000;
            var insuranceInline = (int)((long)amount * Rate / 10_000L);
            var netRental = amount - benefitDiscount;
            var subtotalInline = netRental + insuranceInline;
            var depositInline = insuranceInline > 0 ? 0 : depositTotal;
            var serviceInline = (int)((long)subtotalInline * ServiceBps / 10_000L);
            var riderInline = (int)((long)serviceInline * RiderPaysAll / 10_000L);

            var fee = RentalCharge.InsuranceFor(amount, Rate, offered: true, taken: true);
            var c = RentalCharge.WithInsurance(netRental, fee, ServiceBps, RiderPaysAll, depositTotal);

            Assert.That(fee, Is.EqualTo(insuranceInline));
            Assert.That(c.ServiceChargeCents, Is.EqualTo(serviceInline));
            Assert.That(c.RiderServiceChargeCents, Is.EqualTo(riderInline));
            Assert.That(c.DepositCents, Is.EqualTo(depositInline));
            Assert.That(c.RentalAmountCents, Is.EqualTo(subtotalInline + riderInline));
        }
    }
}
