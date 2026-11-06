using NUnit.Framework;
using Services.BikeShop;

namespace UnitTests
{
    // The decisions behind used & ex-rental listings (Script0302), pinned down without a database:
    // when an online checkout still holds the bike, what a listing's state is, which units may be
    // listed or rung up, what a listing is called, and what prices are accepted.
    [TestFixture]
    public class ListingRulesTests
    {
        private static readonly DateTime Now = new(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc);
        private static readonly Guid SaleId = Guid.NewGuid();

        // ── Reservation live-ness ─────────────────────────────────────────────────

        [Test]
        public void Reservation_NoSale_IsNotLive()
        {
            Assert.That(ListingRules.IsReservationLive(null, null, null, Now), Is.False);
            Assert.That(ListingRules.IsReservationLive(SaleId, null, "pending", Now), Is.False, "no reserved_at");
            Assert.That(ListingRules.IsReservationLive(SaleId, Now, null, Now), Is.False, "sale row gone");
        }

        [Test]
        public void Reservation_FreshPending_IsLive()
        {
            Assert.That(ListingRules.IsReservationLive(SaleId, Now.AddMinutes(-1), "pending", Now), Is.True);
            Assert.That(ListingRules.IsReservationLive(SaleId, Now.AddMinutes(-19), "pending", Now), Is.True);
        }

        [Test]
        public void Reservation_OlderThanTwentyMinutes_IsStale()
        {
            Assert.That(ListingRules.IsReservationLive(SaleId, Now.AddMinutes(-20), "pending", Now), Is.False, "exactly 20 minutes is stale");
            Assert.That(ListingRules.IsReservationLive(SaleId, Now.AddMinutes(-45), "pending", Now), Is.False);
        }

        [TestCase("cancelled")]
        [TestCase("failed")]
        [TestCase("abandoned")]
        [TestCase("refunded")]
        public void Reservation_DeadSale_IsNotLive_EvenWhenFresh(string status)
        {
            Assert.That(ListingRules.IsReservationLive(SaleId, Now.AddSeconds(-30), status, Now), Is.False);
            Assert.That(ListingRules.IsDeadSaleStatus(status), Is.True);
        }

        [Test]
        public void Reservation_PaidSale_StaysLive_HoweverOld()
        {
            // Paid means the unit is about to read 'sold'; nobody else may take it meanwhile.
            Assert.That(ListingRules.IsReservationLive(SaleId, Now.AddHours(-3), "paid", Now), Is.True);
        }

        [Test]
        public void ReservationExpiresAt_IsTwentyMinutesAfterReservedAt()
        {
            Assert.That(ListingRules.ReservationExpiresAtUtc(Now), Is.EqualTo(Now.AddMinutes(20)));
            Assert.That(ListingRules.ReservationExpiresAtUtc(null), Is.Null);
            Assert.That(ListingRules.ReservationMinutes, Is.EqualTo(20));
        }

        // ── State ─────────────────────────────────────────────────────────────────

        [Test]
        public void State_UnlistedWinsOverEverything()
        {
            Assert.That(ListingRules.State(Now, "sold", reservationLive: true), Is.EqualTo("unlisted"));
            Assert.That(ListingRules.State(Now, "for_sale", reservationLive: false), Is.EqualTo("unlisted"));
        }

        [Test]
        public void State_SoldUnit_IsSold()
        {
            Assert.That(ListingRules.State(null, "sold", reservationLive: false), Is.EqualTo("sold"));
            Assert.That(ListingRules.State(null, "sold", reservationLive: true), Is.EqualTo("sold"));
        }

        [Test]
        public void State_OnTheFloor_ActiveOrReserved()
        {
            Assert.That(ListingRules.State(null, "for_sale", reservationLive: false), Is.EqualTo("active"));
            Assert.That(ListingRules.State(null, "for_sale", reservationLive: true), Is.EqualTo("reserved"));
        }

        [TestCase("available")]
        [TestCase("maintenance")]
        [TestCase("rented_out")]
        [TestCase("retired")]
        public void State_UnitLeftTheFloorAnotherWay_ReadsUnlisted(string itemStatus)
        {
            // An open row whose unit an admin moved, or a refund returned to the fleet: it is not
            // for sale, whatever unlisted_at says.
            Assert.That(ListingRules.State(null, itemStatus, reservationLive: false), Is.EqualTo("unlisted"));
        }

        // ── Which units may be listed / rung up ───────────────────────────────────

        [TestCase("available", true)]
        [TestCase("maintenance", true)]
        [TestCase("retired", true)]
        [TestCase("rented_out", false)]
        [TestCase("sold", false)]
        [TestCase("for_sale", false)]
        [TestCase(null, false)]
        public void CanList(string? status, bool expected)
        {
            Assert.That(ListingRules.CanList(status), Is.EqualTo(expected));
        }

        [Test]
        public void CannotListReason_NamesTheState()
        {
            Assert.That(ListingRules.CannotListReason("rented_out"), Does.Contain("rental"));
            Assert.That(ListingRules.CannotListReason("sold"), Does.Contain("sold"));
            Assert.That(ListingRules.CannotListReason("for_sale"), Does.Contain("already listed"));
            Assert.That(ListingRules.CannotListReason("whatever"), Is.Not.Empty);
        }

        [TestCase("available", true)]
        [TestCase("for_sale", true)]
        [TestCase("maintenance", false)]
        [TestCase("retired", false)]
        [TestCase("rented_out", false)]
        [TestCase("sold", false)]
        public void IsSellableAtRegister(string status, bool expected)
        {
            Assert.That(ListingRules.IsSellableAtRegister(status), Is.EqualTo(expected));
        }

        // ── Price validation ──────────────────────────────────────────────────────

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(-180000)]
        public void ValidatePrice_ZeroOrNegative_Rejected(int cents)
        {
            Assert.That(ListingRules.ValidatePrice(cents), Does.Contain("above $0"));
        }

        [Test]
        public void ValidatePrice_AcceptsSensibleAmounts_RejectsTypos()
        {
            Assert.That(ListingRules.ValidatePrice(1), Is.Null);
            Assert.That(ListingRules.ValidatePrice(180_000), Is.Null);
            Assert.That(ListingRules.ValidatePrice(ListingRules.MaxAskingPriceCents), Is.Null);
            Assert.That(ListingRules.ValidatePrice(ListingRules.MaxAskingPriceCents + 1), Is.Not.Null);
        }

        [Test]
        public void Grades_AndConditions()
        {
            Assert.That(ListingRules.IsValidGrade("excellent"), Is.True);
            Assert.That(ListingRules.IsValidGrade("good"), Is.True);
            Assert.That(ListingRules.IsValidGrade("fair"), Is.True);
            Assert.That(ListingRules.IsValidGrade("mint"), Is.False);
            Assert.That(ListingRules.IsValidGrade(null), Is.False);
            Assert.That(ListingRules.IsValidCondition("new"), Is.True);
            Assert.That(ListingRules.IsValidCondition("used"), Is.True);
            Assert.That(ListingRules.IsValidCondition("refurbished"), Is.False);
        }

        // ── Titles and labels ─────────────────────────────────────────────────────

        [Test]
        public void DefaultTitle_ProductVariantAndUnit()
        {
            Assert.That(ListingRules.DefaultTitle("Trek Fuel EX 9.8", "M", "Trek Fuel #3"),
                Is.EqualTo("Trek Fuel EX 9.8 M · Trek Fuel #3"));
        }

        [Test]
        public void DefaultTitle_SkipsBlankParts_AndTrims()
        {
            Assert.That(ListingRules.DefaultTitle(" Trek Fuel EX 9.8 ", null, "Trek Fuel #3"), Is.EqualTo("Trek Fuel EX 9.8 · Trek Fuel #3"));
            Assert.That(ListingRules.DefaultTitle("Trek Fuel EX 9.8", "  ", ""), Is.EqualTo("Trek Fuel EX 9.8"));
            Assert.That(ListingRules.DefaultTitle("Trek Fuel EX 9.8", "M / Red", null), Is.EqualTo("Trek Fuel EX 9.8 M / Red"));
        }

        [Test]
        public void VariantLabel_JoinsWhatIsThere()
        {
            Assert.That(ListingRules.VariantLabel("M", null, null), Is.EqualTo("M"));
            Assert.That(ListingRules.VariantLabel("M", "Red", null), Is.EqualTo("M / Red"));
            Assert.That(ListingRules.VariantLabel(" M ", "", "Women's"), Is.EqualTo("M / Women's"));
            Assert.That(ListingRules.VariantLabel(null, null, null), Is.EqualTo(""));
        }

        // ── Suggested asking price ────────────────────────────────────────────────

        [TestCase(null, null)]
        [TestCase(0, null)]
        [TestCase(-5, null)]
        [TestCase(120_000, 120_000)]
        [TestCase(120_001, 120_500)]
        [TestCase(120_499, 120_500)]
        [TestCase(120_500, 120_500)]
        [TestCase(1, 500)]
        public void SuggestedAskingPrice_RoundsCostUpToNearestFiveDollars(int? cost, int? expected)
        {
            Assert.That(ListingRules.SuggestedAskingPriceCents(cost), Is.EqualTo(expected));
        }

        // ── Unlisting puts the unit back where it came from ──────────────────────

        [TestCase("available", "available")]
        [TestCase("maintenance", "maintenance")]
        [TestCase("retired", "retired")]
        [TestCase(null, "available")]
        [TestCase("for_sale", "available")]
        [TestCase("rented_out", "available")]
        [TestCase("sold", "available")]
        [TestCase("nonsense", "available")]
        public void RestoreStatusOnUnlist(string? prior, string expected)
        {
            // A retired bike that did not sell stays out of the fleet; an old row (null) or anything
            // that is not a listable status goes back to the fleet.
            Assert.That(ListingRules.RestoreStatusOnUnlist(prior), Is.EqualTo(expected));
        }

        // ── A booked rental blocks listing, named by its local date ──────────────

        [Test]
        public void RentalBookedMessage_UtcTenant()
        {
            var starts = new DateTime(2026, 11, 7, 16, 0, 0, DateTimeKind.Utc);
            Assert.That(ListingRules.RentalBookedMessage(starts, "UTC"),
                Is.EqualTo("This bike is booked for a rental on Nov 7, 2026. Move or cancel that booking first."));
            Assert.That(ListingRules.RentalBookedMessage(starts, null), Does.Contain("Nov 7, 2026"), "missing zone reads as UTC");
        }

        [Test]
        public void RentalBookedMessage_UsesTheTenantsLocalDate()
        {
            // 02:00 UTC on Nov 8 is still the evening of Nov 7 in Denver (UTC-7 after DST ends).
            var starts = new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Utc);
            Assert.That(ListingRules.RentalBookedMessage(starts, "America/Denver"), Does.Contain("Nov 7, 2026"));
            Assert.That(ListingRules.RentalBookedMessage(starts, "UTC"), Does.Contain("Nov 8, 2026"));
        }

        [Test]
        public void RentalBookedMessage_UnspecifiedKindIsTreatedAsUtc()
        {
            // Npgsql can hand a timestamptz back Unspecified depending on settings; it is still UTC.
            var starts = new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Unspecified);
            Assert.That(ListingRules.RentalBookedMessage(starts, "America/Denver"), Does.Contain("Nov 7, 2026"));
        }

        // ── Whose hold, and can the buyer resume it ──────────────────────────────

        [Test]
        public void HoldReason_SameBuyerIsHeldByYou_AnyoneElseHeldByOther()
        {
            var me = Guid.NewGuid();
            Assert.That(ListingRules.HoldReason(me, me), Is.EqualTo("held_by_you"));
            Assert.That(ListingRules.HoldReason(Guid.NewGuid(), me), Is.EqualTo("held_by_other"));
            Assert.That(ListingRules.HoldReason(null, me), Is.EqualTo("held_by_other"), "an unknown buyer is never you");
        }

        [TestCase("requires_payment_method", true)]
        [TestCase("requires_confirmation", true)]
        [TestCase("requires_action", true)]
        [TestCase("processing", false)]
        [TestCase("succeeded", false)]
        [TestCase("canceled", false)]
        [TestCase("requires_capture", false)]
        [TestCase(null, false)]
        public void IsResumable_OnlyWhileTheIntentWaitsOnTheBuyer(string? status, bool expected)
        {
            Assert.That(ListingRules.IsResumable(status), Is.EqualTo(expected));
        }
    }
}
