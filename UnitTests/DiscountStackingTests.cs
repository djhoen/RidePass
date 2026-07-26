using NUnit.Framework;
using Services.Discounts;

namespace UnitTests
{
    /// <summary>
    /// Stacking decides how much money comes off a sale, so both directions of error cost real
    /// money: too many discounts compounding, or a customer's entitlement silently dropped.
    /// </summary>
    [TestFixture]
    public class DiscountStackingTests
    {
        [Test]
        public void Stacking_allowed_sums_every_discount()
        {
            var r = DiscountStacking.Resolve(benefitCents: 500, staffCents: 300, couponCents: 200, allowStacking: true);
            Assert.Multiple(() =>
            {
                Assert.That(r.BenefitCents, Is.EqualTo(500));
                Assert.That(r.StaffCents, Is.EqualTo(300));
                Assert.That(r.CouponCents, Is.EqualTo(200));
                Assert.That(r.Total, Is.EqualTo(1000));
            });
        }

        [Test]
        public void Stacking_off_keeps_only_the_largest()
        {
            var r = DiscountStacking.Resolve(500, 300, 200, allowStacking: false);
            Assert.Multiple(() =>
            {
                Assert.That(r.Total, Is.EqualTo(500), "the customer still gets the best deal available");
                Assert.That(r.BenefitCents, Is.EqualTo(500));
                Assert.That(r.StaffCents, Is.Zero);
                Assert.That(r.CouponCents, Is.Zero);
            });
        }

        [Test]
        public void Stacking_off_lets_a_staff_discount_win_when_it_is_biggest()
        {
            var r = DiscountStacking.Resolve(100, 900, 200, allowStacking: false);
            Assert.Multiple(() =>
            {
                Assert.That(r.StaffCents, Is.EqualTo(900));
                Assert.That(r.BenefitCents, Is.Zero);
                Assert.That(r.CouponCents, Is.Zero);
            });
        }

        [Test]
        public void Stacking_off_lets_a_coupon_win_when_it_is_biggest()
        {
            var r = DiscountStacking.Resolve(100, 200, 900, allowStacking: false);
            Assert.Multiple(() =>
            {
                Assert.That(r.CouponCents, Is.EqualTo(900));
                Assert.That(r.BenefitCents, Is.Zero);
                Assert.That(r.StaffCents, Is.Zero);
            });
        }

        [Test]
        public void A_tie_goes_to_the_entitlement_the_customer_already_paid_for()
        {
            var r = DiscountStacking.Resolve(500, 500, 500, allowStacking: false);
            Assert.Multiple(() =>
            {
                Assert.That(r.BenefitCents, Is.EqualTo(500), "a season pass beats a promotion on a tie");
                Assert.That(r.StaffCents, Is.Zero);
                Assert.That(r.CouponCents, Is.Zero);
            });
        }

        [Test]
        public void A_tie_between_staff_and_coupon_goes_to_the_staff_discount()
        {
            var r = DiscountStacking.Resolve(0, 400, 400, allowStacking: false);
            Assert.Multiple(() =>
            {
                Assert.That(r.StaffCents, Is.EqualTo(400));
                Assert.That(r.CouponCents, Is.Zero);
            });
        }

        [Test]
        public void No_discounts_stays_zero_either_way()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DiscountStacking.Resolve(0, 0, 0, false).Total, Is.Zero);
                Assert.That(DiscountStacking.Resolve(0, 0, 0, true).Total, Is.Zero);
            });
        }

        [Test]
        public void A_single_discount_survives_regardless_of_policy()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DiscountStacking.Resolve(0, 250, 0, false).Total, Is.EqualTo(250));
                Assert.That(DiscountStacking.Resolve(0, 250, 0, true).Total, Is.EqualTo(250));
            });
        }

        [Test]
        public void Negative_inputs_are_floored_rather_than_handing_money_back()
        {
            // A negative would otherwise ADD to the price when stacked, or poison the max.
            var stacked = DiscountStacking.Resolve(-100, 300, -50, allowStacking: true);
            var single = DiscountStacking.Resolve(-100, -300, -50, allowStacking: false);
            Assert.Multiple(() =>
            {
                Assert.That(stacked.Total, Is.EqualTo(300));
                Assert.That(single.Total, Is.Zero);
            });
        }
    }
}
