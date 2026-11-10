using NUnit.Framework;
using Services.Pricing;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.PricingData;

namespace UnitTests
{
    // Per-day prices (Script0316): online vs walk-up, and peak days on day passes.
    [TestFixture]
    public class DayPricingTests
    {
        private static readonly DateOnly Saturday = new(2026, 7, 4);   // a Saturday
        private static readonly DateOnly Tuesday = new(2026, 7, 7);
        private static readonly Guid LiftDay = Guid.NewGuid();

        private static EventTicketTier DayPass(int price = 4000, int? walkUp = null) =>
            new() { Kind = "gate_fee", Audience = "rider", PriceCents = price, WalkUpPriceCents = walkUp };

        private static DayPriceRule Weekday(int day, int add, string label = "Weekend", Guid? type = null) =>
            new() { Label = label, Weekday = day, AdjustKind = "amount", AdjustValue = add, IsActive = true, EventTypeId = type };

        private static DayPriceRule Date(DateOnly d, string kind, int value, string label = "Holiday") =>
            new() { Label = label, OnDate = d.ToDateTime(TimeOnly.MinValue), AdjustKind = kind, AdjustValue = value, IsActive = true };

        [Test]
        public void Online_price_with_no_rules()
        {
            var r = DayPricing.For(DayPass(), Tuesday, LiftDay, DayPricing.Online, new List<DayPriceRule>());
            Assert.That((r.PriceCents, r.RuleLabel), Is.EqualTo((4000, (string?)null)));
        }

        [Test]
        public void Walk_up_uses_its_own_price_and_online_does_not()
        {
            var tier = DayPass(4000, walkUp: 4500);
            Assert.That(DayPricing.For(tier, Tuesday, LiftDay, DayPricing.WalkUp, new List<DayPriceRule>()).PriceCents, Is.EqualTo(4500));
            Assert.That(DayPricing.For(tier, Tuesday, LiftDay, DayPricing.Online, new List<DayPriceRule>()).PriceCents, Is.EqualTo(4000));
        }

        [Test]
        public void Walk_up_without_its_own_price_is_the_online_price() =>
            Assert.That(DayPricing.For(DayPass(4000), Tuesday, LiftDay, DayPricing.WalkUp, new List<DayPriceRule>()).PriceCents, Is.EqualTo(4000));

        [Test]
        public void A_weekday_rule_raises_that_day_only()
        {
            var rules = new List<DayPriceRule> { Weekday(6, 1000) };
            Assert.That(DayPricing.For(DayPass(), Saturday, LiftDay, DayPricing.Online, rules).PriceCents, Is.EqualTo(5000));
            Assert.That(DayPricing.For(DayPass(), Tuesday, LiftDay, DayPricing.Online, rules).PriceCents, Is.EqualTo(4000));
        }

        [Test]
        public void Peak_day_raises_the_walk_up_price_too()
        {
            var r = DayPricing.For(DayPass(4000, walkUp: 4500), Saturday, LiftDay, DayPricing.WalkUp, new List<DayPriceRule> { Weekday(6, 1000) });
            Assert.That((r.PriceCents, r.BaseCents, r.RuleLabel), Is.EqualTo((5500, 4500, "Weekend")));
        }

        [Test]
        public void A_date_rule_beats_weekday_rules_even_when_smaller()
        {
            var rules = new List<DayPriceRule> { Weekday(6, 1000), Date(Saturday, "amount", 500, "July 4th") };
            var r = DayPricing.For(DayPass(), Saturday, LiftDay, DayPricing.Online, rules);
            Assert.That((r.PriceCents, r.RuleLabel), Is.EqualTo((4500, "July 4th")));
        }

        [Test]
        public void Rules_do_not_stack_the_biggest_wins()
        {
            var rules = new List<DayPriceRule> { Weekday(6, 500, "A"), Weekday(6, 1500, "B") };
            Assert.That(DayPricing.For(DayPass(), Saturday, LiftDay, DayPricing.Online, rules).PriceCents, Is.EqualTo(5500));
        }

        [Test]
        public void Percent_rules_are_basis_points() =>
            Assert.That(DayPricing.For(DayPass(4000), Saturday, LiftDay, DayPricing.Online,
                new List<DayPriceRule> { Date(Saturday, "percent", 2500) }).PriceCents, Is.EqualTo(5000));

        [Test]
        public void A_rule_for_another_event_type_does_not_apply() =>
            Assert.That(DayPricing.For(DayPass(), Saturday, LiftDay, DayPricing.Online,
                new List<DayPriceRule> { Weekday(6, 1000, type: Guid.NewGuid()) }).PriceCents, Is.EqualTo(4000));

        [Test]
        public void Inactive_rules_do_not_apply()
        {
            var rule = Weekday(6, 1000);
            rule.IsActive = false;
            Assert.That(DayPricing.For(DayPass(), Saturday, LiftDay, DayPricing.Online, new List<DayPriceRule> { rule }).PriceCents, Is.EqualTo(4000));
        }

        [Test]
        public void Race_entries_and_free_tickets_are_never_raised()
        {
            var rules = new List<DayPriceRule> { Weekday(6, 1000) };
            var race = new EventTicketTier { Kind = "race_entry", Audience = "rider", PriceCents = 6000 };
            Assert.That(DayPricing.For(race, Saturday, LiftDay, DayPricing.Online, rules).PriceCents, Is.EqualTo(6000));
            Assert.That(DayPricing.For(DayPass(0), Saturday, LiftDay, DayPricing.Online, rules).PriceCents, Is.EqualTo(0));
        }
    }
}
