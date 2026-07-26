using NUnit.Framework;
using Services.Pricing;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.TenantData;

namespace UnitTests
{
    // The one rule that decides how much money comes off a sale for a pass holder, now shared by
    // five tills (F&B, shop counter, shop online, rental counter, rental online). Before this was
    // centralized each till had its own copy, so the interesting cases are the ones where a copy
    // could plausibly have differed: precedence, surface scoping, and stacking.
    [TestFixture]
    public class SeasonPassPerkTests
    {
        private static Tenant TenantWith(
            bool enabled = true, string kind = "percent", int value = 1000,
            bool concession = true, bool retail = true, bool rental = true) => new()
            {
                Id = Guid.NewGuid(),
                SeasonPassDiscountEnabled = enabled,
                SeasonPassDiscountKind = kind,
                SeasonPassDiscountValue = value,
                SeasonPassDiscountAppliesConcession = concession,
                SeasonPassDiscountAppliesRetail = retail,
                SeasonPassDiscountAppliesRental = rental,
            };

        private static SeasonPassBenefitGrant Grant(string product, string kind, int value) => new()
        {
            PassPurchaseId = Guid.NewGuid(),
            ProductName = product,
            Benefit = new SeasonPassBenefit { DiscountKind = kind, DiscountValue = value },
        };

        private static readonly SeasonPassBenefitGrant[] NoGrants = Array.Empty<SeasonPassBenefitGrant>();

        // ── Tenant-wide discount ────────────────────────────────────────────────

        [Test]
        public void HolderGetsTheTenantWideDiscount()
        {
            var perk = SeasonPassPerkResolver.Decide(NoGrants, TenantWith(value: 1000), "retail", 10_000, holdsActivePass: true);
            Assert.Multiple(() =>
            {
                Assert.That(perk.DiscountCents, Is.EqualTo(1_000), "10% of $100");
                Assert.That(perk.IsPerPass, Is.False);
                Assert.That(perk.Label, Is.EqualTo("Season Pass discount"));
            });
        }

        [Test]
        public void NonHolderGetsNothing()
        {
            var perk = SeasonPassPerkResolver.Decide(NoGrants, TenantWith(), "retail", 10_000, holdsActivePass: false);
            Assert.That(perk.Any, Is.False);
        }

        [Test]
        public void SwitchedOffMeansNothing()
        {
            var perk = SeasonPassPerkResolver.Decide(NoGrants, TenantWith(enabled: false), "retail", 10_000, holdsActivePass: true);
            Assert.That(perk.Any, Is.False);
        }

        // The reason surfaces are separate: 15% chosen for a $9 burger is 15% off a $6,000 bike.
        [Test]
        public void SurfaceFlagsScopeTheDiscount()
        {
            var t = TenantWith(value: 1500, concession: true, retail: false, rental: false);
            Assert.Multiple(() =>
            {
                Assert.That(SeasonPassPerkResolver.Decide(NoGrants, t, "concession", 10_000, true).DiscountCents,
                    Is.EqualTo(1_500), "F&B is on");
                Assert.That(SeasonPassPerkResolver.Decide(NoGrants, t, "retail", 10_000, true).Any,
                    Is.False, "retail is off");
                Assert.That(SeasonPassPerkResolver.Decide(NoGrants, t, "rental", 10_000, true).Any,
                    Is.False, "rentals are off");
            });
        }

        // A standing "% off for holders" must never quietly become a discount on race entry.
        [Test]
        public void EventsAreNeverCoveredByTheTenantWideDiscount()
        {
            var perk = SeasonPassPerkResolver.Decide(NoGrants, TenantWith(), "event", 10_000, holdsActivePass: true);
            Assert.That(perk.Any, Is.False);
        }

        // ── Per-pass benefits ───────────────────────────────────────────────────

        [Test]
        public void PerPassBenefitBeatsTheTenantWideDiscount()
        {
            var grants = new[] { Grant("Employee Pass", "percent", 5000) };
            var perk = SeasonPassPerkResolver.Decide(grants, TenantWith(value: 1000), "concession", 10_000, true);
            Assert.Multiple(() =>
            {
                Assert.That(perk.DiscountCents, Is.EqualTo(5_000), "50%, not the tenant's 10%");
                Assert.That(perk.IsPerPass, Is.True);
                Assert.That(perk.Label, Is.EqualTo("Employee Pass discount"), "the receipt should name the perk");
            });
        }

        // They are not summed. An employee pass plus a tenant-wide discount must not compound, or a
        // $6,000 bike goes out at 60% off by accident.
        [Test]
        public void PerkIsTheBetterArrangement_NotBoth()
        {
            var grants = new[] { Grant("Employee Pass", "percent", 5000) };
            var perk = SeasonPassPerkResolver.Decide(grants, TenantWith(value: 1000), "retail", 10_000, true);
            Assert.That(perk.DiscountCents, Is.EqualTo(5_000), "50%, not 60%");
        }

        // Product configuration, not the loyalty scheme: an employee pass's perks must survive the
        // tenant switching its holder discount off.
        [Test]
        public void PerPassBenefitSurvivesTheTenantSwitchBeingOff()
        {
            var grants = new[] { Grant("Employee Pass", "percent", 5000) };
            var t = TenantWith(enabled: false, concession: false, retail: false, rental: false);
            var perk = SeasonPassPerkResolver.Decide(grants, t, "retail", 10_000, holdsActivePass: false);
            Assert.Multiple(() =>
            {
                Assert.That(perk.DiscountCents, Is.EqualTo(5_000));
                Assert.That(perk.IsPerPass, Is.True);
            });
        }

        // Best against THIS sale, not a nominal one. On a $30 sale $5-off beats 10%...
        [Test]
        public void BestGrantIsChosenAgainstTheActualSale_SmallBasket()
        {
            var grants = new[] { Grant("Pass A", "percent", 1000), Grant("Pass B", "amount", 500) };
            var perk = SeasonPassPerkResolver.Decide(grants, TenantWith(enabled: false), "retail", 3_000, false);
            Assert.Multiple(() =>
            {
                Assert.That(perk.DiscountCents, Is.EqualTo(500), "$5 off beats 10% of $30");
                Assert.That(perk.Label, Is.EqualTo("Pass B discount"));
            });
        }

        // ...and on a $300 sale 10% wins.
        [Test]
        public void BestGrantIsChosenAgainstTheActualSale_LargeBasket()
        {
            var grants = new[] { Grant("Pass A", "percent", 1000), Grant("Pass B", "amount", 500) };
            var perk = SeasonPassPerkResolver.Decide(grants, TenantWith(enabled: false), "retail", 30_000, false);
            Assert.Multiple(() =>
            {
                Assert.That(perk.DiscountCents, Is.EqualTo(3_000), "10% of $300 beats $5 off");
                Assert.That(perk.Label, Is.EqualTo("Pass A discount"));
            });
        }

        // A grant row can exist at zero value; it must not shadow the tenant-wide discount, or
        // configuring a 0% perk would silently cancel the track's loyalty scheme.
        [Test]
        public void ZeroValueGrantFallsThroughToTheTenantWideDiscount()
        {
            var grants = new[] { Grant("Pass A", "percent", 0) };
            var perk = SeasonPassPerkResolver.Decide(grants, TenantWith(value: 1000), "retail", 10_000, true);
            Assert.Multiple(() =>
            {
                Assert.That(perk.DiscountCents, Is.EqualTo(1_000));
                Assert.That(perk.IsPerPass, Is.False);
            });
        }

        // ── Arithmetic ──────────────────────────────────────────────────────────

        [Test]
        public void DiscountNeverExceedsTheSale()
        {
            // $20 off a $5 sale takes it to zero, it does not owe the customer money.
            var perk = SeasonPassPerkResolver.Decide(NoGrants, TenantWith(kind: "amount", value: 2_000), "retail", 500, true);
            Assert.That(perk.DiscountCents, Is.EqualTo(500));
        }

        [Test]
        public void PercentIsBasisPointsAndRoundsHalfUp()
        {
            // 7.5% of $1.01 = 7.575 cents -> 8.
            var perk = SeasonPassPerkResolver.Decide(NoGrants, TenantWith(value: 750), "retail", 101, true);
            Assert.That(perk.DiscountCents, Is.EqualTo(8));
        }

        [Test]
        public void PercentIsCappedAtOneHundred()
        {
            var perk = SeasonPassPerkResolver.Decide(NoGrants, TenantWith(value: 20_000), "retail", 10_000, true);
            Assert.That(perk.DiscountCents, Is.EqualTo(10_000), "a bad value can't exceed the sale");
        }

        // ── The sale's discount_label snapshot ──────────────────────────────────
        // A pass perk applies with no staff action, so without this a $600 line on a sale has no
        // explanation on it at all.

        [Test]
        public void LabelNamesThePerkWhenItIsTheOnlyDiscount()
        {
            var perk = new SeasonPassPerk(5_000, "Employee Pass discount", IsPerPass: true);
            Assert.That(SeasonPassPerk.LabelFor(perk, 5_000, null, 0), Is.EqualTo("Employee Pass discount"));
        }

        [Test]
        public void LabelNamesBothWhenBothStacked()
        {
            var perk = new SeasonPassPerk(1_000, "Season Pass discount", IsPerPass: false);
            Assert.That(SeasonPassPerk.LabelFor(perk, 1_000, "Military 10%", 900),
                Is.EqualTo("Season Pass discount + Military 10%"));
        }

        // Stacking off: exactly one survives, and labelling the loser would describe a discount the
        // customer never received.
        [Test]
        public void LabelOmitsTheDiscountThatLostStacking()
        {
            var perk = new SeasonPassPerk(1_000, "Season Pass discount", IsPerPass: false);
            Assert.Multiple(() =>
            {
                Assert.That(SeasonPassPerk.LabelFor(perk, 0, "Military 10%", 900), Is.EqualTo("Military 10%"),
                    "the perk was zeroed by stacking");
                Assert.That(SeasonPassPerk.LabelFor(perk, 1_000, "Military 10%", 0),
                    Is.EqualTo("Season Pass discount"), "the staff discount was zeroed");
            });
        }

        [Test]
        public void LabelIsNullWhenNothingWasDiscounted()
        {
            Assert.That(SeasonPassPerk.LabelFor(SeasonPassPerk.None, 0, null, 0), Is.Null);
        }

        [Test]
        public void ZeroOrNegativeBaseYieldsNothing()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SeasonPassPerkResolver.Decide(NoGrants, TenantWith(), "retail", 0, true).Any, Is.False);
                Assert.That(SeasonPassPerkResolver.Decide(
                    new[] { Grant("Pass A", "percent", 5000) }, TenantWith(), "retail", 0, true).Any, Is.False);
            });
        }
    }
}
