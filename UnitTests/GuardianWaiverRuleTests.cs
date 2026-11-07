using NUnit.Framework;
using Services.Helpers;
using Services.Waivers;

namespace UnitTests
{
    // The gate's minor / guardian rule. A wrong answer either lets a minor in on their own signature
    // or turns away a rider who is fine, so the age arithmetic and the evidence rules are pinned here.
    [TestFixture]
    public class GuardianWaiverRuleTests
    {
        private static readonly DateOnly Day = new(2026, 11, 7);
        private static readonly WaiverSignatureFacts SelfSigned = new(true, false, null, "Rowan Zimmerman", null);
        private static readonly WaiverSignatureFacts GuardianSigned = new(true, true, "Devon Zimmerman", "Rowan Zimmerman", null);

        [Test]
        public void Exactly_eighteen_today_is_an_adult_and_the_day_before_is_a_minor()
        {
            var dob = new DateOnly(2008, 11, 7);
            Assert.That(GuardianWaiverRule.AgeOn(dob, Day), Is.EqualTo(18));
            Assert.That(GuardianWaiverRule.IsMinorOn(dob, Day), Is.False);
            Assert.That(GuardianWaiverRule.IsMinorOn(dob, Day.AddDays(-1)), Is.True);
            Assert.That(GuardianWaiverRule.IsMinorOn(new DateOnly(2008, 11, 8), Day), Is.True);
        }

        [Test]
        public void Leap_day_birthday_turns_over_on_march_first_in_a_common_year()
        {
            var dob = new DateOnly(2008, 2, 29);
            Assert.That(GuardianWaiverRule.AgeOn(dob, new DateOnly(2026, 2, 28)), Is.EqualTo(17));
            Assert.That(GuardianWaiverRule.AgeOn(dob, new DateOnly(2026, 3, 1)), Is.EqualTo(18));
            // In a leap year the birthday itself exists.
            Assert.That(GuardianWaiverRule.AgeOn(new DateOnly(2010, 2, 28), new DateOnly(2028, 2, 28)), Is.EqualTo(18));
            Assert.That(GuardianWaiverRule.AgeOn(new DateOnly(2012, 2, 29), new DateOnly(2030, 2, 28)), Is.EqualTo(17));
            Assert.That(GuardianWaiverRule.AgeOn(new DateOnly(2012, 2, 29), new DateOnly(2032, 2, 29)), Is.EqualTo(20));
        }

        [Test]
        public void Age_is_judged_on_the_tenant_local_date_not_utc()
        {
            // 03:00 UTC on 8 Nov is still the evening of 7 Nov in New York.
            var nowUtc = new DateTime(2026, 11, 8, 3, 0, 0, DateTimeKind.Utc);
            var dob = new DateOnly(2008, 11, 8);   // 18th birthday is 8 Nov
            var nyToday = TenantClock.LocalDate("America/New_York", nowUtc);
            var utcToday = TenantClock.LocalDate("UTC", nowUtc);
            Assert.That(nyToday, Is.EqualTo(new DateOnly(2026, 11, 7)));
            Assert.That(GuardianWaiverRule.IsMinorOn(dob, nyToday), Is.True);
            Assert.That(GuardianWaiverRule.IsMinorOn(dob, utcToday), Is.False);
        }

        [Test]
        public void Minor_who_signed_their_own_waiver_is_blocked_with_the_message()
        {
            var r = GuardianWaiverRule.Evaluate(new DateOnly(2012, 5, 1), Day, SelfSigned, "Rowan Zimmerman");
            Assert.That(r.BlockReason, Is.EqualTo("Rowan Zimmerman is under 18. A parent or guardian has to sign their waiver."));
            Assert.That(r.IsMinor, Is.True);
            Assert.That(r.Age, Is.EqualTo(14));
        }

        [Test]
        public void Minor_with_a_guardian_signature_is_allowed_and_names_the_guardian()
        {
            var r = GuardianWaiverRule.Evaluate(new DateOnly(2012, 5, 1), Day, GuardianSigned, "Rowan Zimmerman");
            Assert.That(r.BlockReason, Is.Null);
            Assert.That(r.SignedByGuardian, Is.True);
            Assert.That(r.GuardianName, Is.EqualTo("Devon Zimmerman"));
        }

        [Test]
        public void Signed_by_parent_without_a_guardian_name_does_not_count()
        {
            var noName = new WaiverSignatureFacts(true, true, "  ", "Rowan", null);
            Assert.That(GuardianWaiverRule.Evaluate(new DateOnly(2012, 5, 1), Day, noName, "Rowan").BlockReason, Is.Not.Null);
        }

        [Test]
        public void Legacy_ticket_without_a_signature_row_uses_its_own_guardian_name()
        {
            var legacy = new WaiverSignatureFacts(false, false, null, null, "Pat Guardian");
            var r = GuardianWaiverRule.Evaluate(new DateOnly(2012, 5, 1), Day, legacy, "Kid");
            Assert.That(r.BlockReason, Is.Null);
            Assert.That(r.GuardianName, Is.EqualTo("Pat Guardian"));
            var legacyNone = new WaiverSignatureFacts(false, false, null, null, null);
            Assert.That(GuardianWaiverRule.Evaluate(new DateOnly(2012, 5, 1), Day, legacyNone, "Kid").BlockReason, Is.Not.Null);
        }

        [Test]
        public void Adult_own_signature_is_allowed()
        {
            var r = GuardianWaiverRule.Evaluate(new DateOnly(1990, 1, 1), Day, SelfSigned, "Adult");
            Assert.That(r.BlockReason, Is.Null);
            Assert.That(r.IsMinor, Is.False);
        }

        [Test]
        public void Unknown_birthdate_is_allowed_but_flagged_not_treated_as_adult()
        {
            var r = GuardianWaiverRule.Evaluate(null, Day, SelfSigned, "Who");
            Assert.That(r.BlockReason, Is.Null);
            Assert.That(r.BirthdateUnknown, Is.True);
            Assert.That(r.IsMinor, Is.Null);
            Assert.That(r.Age, Is.Null);
        }

        [Test]
        public void Birthdate_sources_resolve_in_order()
        {
            var v = new DateOnly(2010, 1, 1);
            var rec = new DateOnly(2011, 1, 1);
            var sig = new DateOnly(2012, 1, 1);
            var accV = new DateOnly(1980, 1, 1);
            var acc = new DateOnly(1981, 1, 1);
            Assert.That(GuardianWaiverRule.ResolveBirthdate(new(v, rec, sig, accV, acc, true)), Is.EqualTo(v));
            Assert.That(GuardianWaiverRule.ResolveBirthdate(new(null, rec, sig, accV, acc, true)), Is.EqualTo(rec));
            Assert.That(GuardianWaiverRule.ResolveBirthdate(new(null, null, sig, accV, acc, true)), Is.EqualTo(sig));
            Assert.That(GuardianWaiverRule.ResolveBirthdate(new(null, null, null, accV, acc, true)), Is.EqualTo(accV));
            Assert.That(GuardianWaiverRule.ResolveBirthdate(new(null, null, null, null, acc, true)), Is.EqualTo(acc));
            // A parent's account never stands in for the kid's birthdate.
            Assert.That(GuardianWaiverRule.ResolveBirthdate(new(null, null, null, accV, acc, false)), Is.Null);
        }

        [Test]
        public void Account_is_the_rider_only_when_names_match_or_no_rider_name()
        {
            Assert.That(GuardianWaiverRule.AccountIsRider(null, " ", "Pat", "Lee"), Is.True);
            Assert.That(GuardianWaiverRule.AccountIsRider(" pat ", "LEE", "Pat", "Lee"), Is.True);
            Assert.That(GuardianWaiverRule.AccountIsRider("Kid", "Lee", "Pat", "Lee"), Is.False);
        }

        [Test]
        public void Guardian_email_is_suggested_only_when_the_buyer_is_someone_else()
        {
            Assert.That(GuardianWaiverRule.SuggestGuardianEmail("Rowan", "Zimmerman", "Devon Zimmerman", "devon@x.test"),
                Is.EqualTo("devon@x.test"));
            Assert.That(GuardianWaiverRule.SuggestGuardianEmail("Rowan", "Zimmerman", "rowan zimmerman", "r@x.test"), Is.Null);
            Assert.That(GuardianWaiverRule.SuggestGuardianEmail(null, null, "Devon", "devon@x.test"), Is.Null);
        }
    }
}
