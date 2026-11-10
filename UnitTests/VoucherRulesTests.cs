using NUnit.Framework;
using Services.Vouchers;

namespace UnitTests
{
    // Vouchers (Script0315): how many a staff member may still give, and which ticket one pays for.
    [TestFixture]
    public class VoucherRulesTests
    {
        [Test]
        public void Admins_are_unlimited_whatever_their_row_says() =>
            Assert.That(VoucherRules.Remaining(isAdmin: true, hasAllowanceRow: true, yearlyAllowance: 2, issuedThisYear: 50), Is.Null);

        [Test]
        public void Staff_without_a_row_have_none() =>
            Assert.That(VoucherRules.Remaining(false, false, null, 0), Is.EqualTo(0));

        [Test]
        public void A_row_with_no_number_is_unlimited() =>
            Assert.That(VoucherRules.Remaining(false, true, null, 12), Is.Null);

        [TestCase(5, 0, 5)]
        [TestCase(5, 3, 2)]
        [TestCase(5, 5, 0)]
        [TestCase(5, 9, 0)]   // allowance lowered after giving: never negative
        public void Allowance_counts_down(int allowance, int issued, int expected) =>
            Assert.That(VoucherRules.Remaining(false, true, allowance, issued), Is.EqualTo(expected));

        [Test]
        public void Picks_the_dearest_rider_admission()
        {
            var pick = VoucherRules.PickUnit(new[] { (0, 1500, true), (1, 4500, true), (2, 3000, true) });
            Assert.That(pick, Is.EqualTo(1));
        }

        [Test]
        public void Never_picks_a_spectator_or_a_free_line()
        {
            var pick = VoucherRules.PickUnit(new[] { (0, 9000, false), (1, 0, true), (2, 1200, true) });
            Assert.That(pick, Is.EqualTo(2));
        }

        [Test]
        public void Ties_go_to_the_first_unit() =>
            Assert.That(VoucherRules.PickUnit(new[] { (0, 1500, true), (1, 1500, true) }), Is.EqualTo(0));

        [Test]
        public void No_rider_admission_means_no_pick() =>
            Assert.That(VoucherRules.PickUnit(new[] { (0, 1500, false) }), Is.Null);

        [TestCase("race_entry", null, true)]
        [TestCase("gate_fee", "rider", true)]
        [TestCase("gate_fee", "spectator", false)]
        public void Rider_admission_kinds(string kind, string? audience, bool expected) =>
            Assert.That(VoucherRules.IsRiderAdmission(kind, audience), Is.EqualTo(expected));

        [Test]
        public void Year_starts_at_local_midnight_on_January_1()
        {
            // 03:00 UTC on Jan 1 is still Dec 31 in New York, so the year is the previous one.
            var start = VoucherRules.YearStartUtc("America/New_York", new DateTime(2027, 1, 1, 3, 0, 0, DateTimeKind.Utc));
            Assert.That(start, Is.EqualTo(new DateTime(2026, 1, 1, 5, 0, 0, DateTimeKind.Utc)));
        }
    }
}
