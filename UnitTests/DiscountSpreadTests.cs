using NUnit.Framework;
using Services.Discounts;

namespace UnitTests
{
    /// <summary>
    /// The per-line amounts this produces are what get stored, taxed and refunded, so the sum has to
    /// be exact and no line may go negative.
    /// </summary>
    [TestFixture]
    public class DiscountSpreadTests
    {
        [Test]
        public void Splits_proportionally_to_each_line()
        {
            var r = DiscountSpread.Across(new[] { 1000, 3000 }, 400);
            Assert.Multiple(() =>
            {
                Assert.That(r[0], Is.EqualTo(100));
                Assert.That(r[1], Is.EqualTo(300));
            });
        }

        [Test]
        public void Always_sums_to_the_discount_even_when_it_does_not_divide_evenly()
        {
            // 100 across three equal lines is 33.33 each; the lost cent must still be handed out.
            var r = DiscountSpread.Across(new[] { 500, 500, 500 }, 100);
            Assert.That(r[0] + r[1] + r[2], Is.EqualTo(100));
        }

        [Test]
        public void The_rounding_remainder_goes_to_the_largest_line()
        {
            var r = DiscountSpread.Across(new[] { 100, 100, 1000 }, 100);
            Assert.Multiple(() =>
            {
                Assert.That(r.Sum(), Is.EqualTo(100));
                Assert.That(r[2], Is.GreaterThan(r[0]), "the biggest line absorbs the odd cents");
            });
        }

        [Test]
        public void Cart_order_does_not_change_the_result()
        {
            var a = DiscountSpread.Across(new[] { 700, 1300, 400 }, 333);
            var b = DiscountSpread.Across(new[] { 400, 1300, 700 }, 333);
            Assert.Multiple(() =>
            {
                Assert.That(a.Sum(), Is.EqualTo(333));
                Assert.That(b.Sum(), Is.EqualTo(333));
                // Same multiset of amounts regardless of how the cashier rang it up.
                Assert.That(a.OrderBy(x => x), Is.EqualTo(b.OrderBy(x => x)));
            });
        }

        [Test]
        public void A_discount_bigger_than_the_eligible_goods_is_clamped_not_overpaid()
        {
            var r = DiscountSpread.Across(new[] { 500, 1000 }, 5000);
            Assert.Multiple(() =>
            {
                Assert.That(r.Sum(), Is.EqualTo(1500), "takes the goods to zero and no further");
                Assert.That(r[0], Is.LessThanOrEqualTo(500));
                Assert.That(r[1], Is.LessThanOrEqualTo(1000));
            });
        }

        [Test]
        public void No_line_ever_exceeds_its_own_base()
        {
            var bases = new[] { 100, 250, 75, 900 };
            var r = DiscountSpread.Across(bases, 900);
            for (var i = 0; i < bases.Length; i++)
                Assert.That(r[i], Is.LessThanOrEqualTo(bases[i]), $"line {i} discounted below zero");
        }

        [Test]
        public void Nothing_to_spread_returns_all_zeroes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(DiscountSpread.Across(new[] { 100, 200 }, 0).Sum(), Is.Zero);
                Assert.That(DiscountSpread.Across(new[] { 100, 200 }, -50).Sum(), Is.Zero);
                Assert.That(DiscountSpread.Across(System.Array.Empty<int>(), 100), Is.Empty);
            });
        }

        [Test]
        public void Free_lines_absorb_nothing_and_do_not_break_the_split()
        {
            var r = DiscountSpread.Across(new[] { 0, 0, 1000 }, 250);
            Assert.Multiple(() =>
            {
                Assert.That(r[0], Is.Zero);
                Assert.That(r[1], Is.Zero);
                Assert.That(r[2], Is.EqualTo(250));
            });
        }

        [Test]
        public void An_all_free_cart_takes_no_discount_rather_than_dividing_by_zero()
        {
            var r = DiscountSpread.Across(new[] { 0, 0 }, 500);
            Assert.That(r.Sum(), Is.Zero);
        }
    }
}
