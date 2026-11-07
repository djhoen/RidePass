using NUnit.Framework;
using Services.Helpers;
using Services.Riders;
using Services.Waivers;

namespace UnitTests
{
    // "Signed on or after their 18th birthday" is a tenant-local CALENDAR comparison. In
    // America/Santiago, DST starts at 00:00 (the clock jumps to 01:00), so local midnight on that day
    // does not exist: converting it to UTC throws. The cutoff must never depend on that instant.
    [TestFixture]
    public class AdultWaiverCutoffTests
    {
        private const string Santiago = "America/Santiago";
        // Chile springs forward on Sunday 2025-09-07: 00:00 -> 01:00 local.
        private static readonly DateOnly DstDay = new(2025, 9, 7);
        private static readonly DateOnly BornOnDstDay = new(2007, 9, 7);

        private static TimeZoneInfo Zone => TenantClock.Resolve(Santiago);

        [Test]
        public void Santiago_local_midnight_on_the_dst_day_does_not_exist()
        {
            // The trap the old code fell into: local midnight -> UTC.
            var midnight = DstDay.ToDateTime(TimeOnly.MinValue);
            Assert.That(Zone.IsInvalidTime(midnight), Is.True);
            Assert.Throws<ArgumentException>(() => TimeZoneInfo.ConvertTimeToUtc(midnight, Zone));
        }

        [Test]
        public void Eighteenth_birthday_on_the_dst_day_is_that_calendar_day()
        {
            Assert.That(GuardianWaiverRule.EighteenthBirthday(BornOnDstDay), Is.EqualTo(DstDay));
        }

        [Test]
        public void A_signature_in_the_first_minutes_of_the_dst_birthday_counts_as_adult()
        {
            // 01:05 local (-03:00, the first valid hour) = 04:05 UTC on the 7th.
            var signedUtc = new DateTime(2025, 9, 7, 4, 5, 0, DateTimeKind.Utc);
            var signedLocal = IdVerificationWindow.VerifiedLocalDate(signedUtc, Zone);
            Assert.That(signedLocal, Is.EqualTo(DstDay));

            var facts = new WaiverSignatureFacts(true, false, null, "Ana Rojas", null, signedLocal);
            var r = GuardianWaiverRule.Evaluate(BornOnDstDay, new DateOnly(2025, 10, 1), facts, "Ana Rojas");
            Assert.That(r.AdultWaiverNeeded, Is.False);
            Assert.That(r.BlockReason, Is.Null);
        }

        [Test]
        public void A_signature_the_evening_before_the_dst_birthday_is_still_minor_era()
        {
            // 23:50 local on the 6th (-04:00) = 03:50 UTC on the 7th: the UTC date is already the
            // birthday, but the tenant's calendar day is not.
            var signedUtc = new DateTime(2025, 9, 7, 3, 50, 0, DateTimeKind.Utc);
            var signedLocal = IdVerificationWindow.VerifiedLocalDate(signedUtc, Zone);
            Assert.That(signedLocal, Is.EqualTo(new DateOnly(2025, 9, 6)));

            var facts = new WaiverSignatureFacts(true, false, null, "Ana Rojas", null, signedLocal);
            var r = GuardianWaiverRule.Evaluate(BornOnDstDay, new DateOnly(2025, 10, 1), facts, "Ana Rojas");
            Assert.That(r.AdultWaiverNeeded, Is.True);
            Assert.That(r.TurnedEighteenOn, Is.EqualTo(DstDay));
        }

        [Test]
        public void Account_sign_paths_treat_a_guardian_signature_as_unsigned_once_the_rider_is_eighteen()
        {
            var guardian = new WaiverSignatureFacts(true, true, "Pat Rojas", null, null, new DateOnly(2025, 3, 1));
            Assert.That(GuardianWaiverRule.IsMinorEraForAdult(BornOnDstDay, new DateOnly(2025, 9, 6), guardian), Is.False);
            Assert.That(GuardianWaiverRule.IsMinorEraForAdult(BornOnDstDay, DstDay, guardian), Is.True);

            var adult = new WaiverSignatureFacts(true, false, null, "Ana Rojas", null, DstDay);
            Assert.That(GuardianWaiverRule.IsMinorEraForAdult(BornOnDstDay, new DateOnly(2025, 10, 1), adult), Is.False);
        }

        [Test]
        public void A_parent_own_signature_is_never_judged_against_the_rider_birthday()
        {
            // applyAdultRule: false is how the pass path evaluates a signature that is not the holder's.
            var parentOwn = new WaiverSignatureFacts(true, false, null, "Pat Rojas", null, new DateOnly(2024, 1, 1));
            var r = GuardianWaiverRule.Evaluate(BornOnDstDay, new DateOnly(2025, 10, 1), parentOwn, "Ana Rojas",
                applyAdultRule: false);
            Assert.That(r.AdultWaiverNeeded, Is.False);
            Assert.That(r.BlockReason, Is.Null);
        }
    }
}
