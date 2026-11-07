using NUnit.Framework;
using Services.Riders;

namespace UnitTests
{
    // "Verified this year": a wrong window either waves through a rider whose ID check has lapsed
    // or re-cards everyone every visit, so the day arithmetic is pinned here.
    [TestFixture]
    public class IdVerificationWindowTests
    {
        private static readonly TimeZoneInfo Ny = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        private static readonly DateOnly CheckIn = new(2026, 11, 7);

        // Noon New York time on the given local day, as UTC.
        private static DateTime NoonNy(DateOnly day) =>
            TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(new TimeOnly(12, 0)), Ny);

        [Test]
        public void Verified_today_is_current()
        {
            Assert.That(IdVerificationWindow.IsCurrent(NoonNy(CheckIn), CheckIn, 365, Ny), Is.True);
        }

        [Test]
        public void Last_day_of_the_window_is_current_and_the_next_day_is_not()
        {
            Assert.That(IdVerificationWindow.IsCurrent(NoonNy(CheckIn.AddDays(-364)), CheckIn, 365, Ny), Is.True);
            Assert.That(IdVerificationWindow.IsCurrent(NoonNy(CheckIn.AddDays(-365)), CheckIn, 365, Ny), Is.False);
            Assert.That(IdVerificationWindow.IsCurrent(NoonNy(CheckIn.AddDays(-400)), CheckIn, 365, Ny), Is.False);
        }

        [Test]
        public void Never_verified_is_not_current()
        {
            Assert.That(IdVerificationWindow.IsCurrent(null, CheckIn, 365, Ny), Is.False);
            Assert.That(IdVerificationWindow.VerifiedLocalDate(null, Ny), Is.Null);
        }

        [Test]
        public void A_verification_dated_after_the_check_in_day_is_not_trusted()
        {
            Assert.That(IdVerificationWindow.IsCurrent(NoonNy(CheckIn.AddDays(1)), CheckIn, 365, Ny), Is.False);
        }

        [Test]
        public void The_day_is_the_tenant_local_day_not_utc()
        {
            // 02:00 UTC on 8 Nov is the evening of 7 Nov in New York: that's the 7th's check.
            var lateEvening = new DateTime(2026, 11, 8, 2, 0, 0, DateTimeKind.Utc);
            Assert.That(IdVerificationWindow.VerifiedLocalDate(lateEvening, Ny), Is.EqualTo(CheckIn));
            Assert.That(IdVerificationWindow.IsCurrent(lateEvening, CheckIn, 365, Ny), Is.True);
            // Read in UTC it would be the 8th, i.e. "in the future" for a 7 Nov check-in.
            Assert.That(IdVerificationWindow.IsCurrent(lateEvening, CheckIn, 365, TimeZoneInfo.Utc), Is.False);
        }

        [Test]
        public void A_one_day_window_means_today_only()
        {
            Assert.That(IdVerificationWindow.IsCurrent(NoonNy(CheckIn), CheckIn, 1, Ny), Is.True);
            Assert.That(IdVerificationWindow.IsCurrent(NoonNy(CheckIn.AddDays(-1)), CheckIn, 1, Ny), Is.False);
        }

        [TestCase("off", true)]
        [TestCase("prompt", true)]
        [TestCase("required", true)]
        [TestCase("Required", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void Only_the_three_modes_are_valid(string? mode, bool valid)
        {
            Assert.That(IdVerificationWindow.IsValidMode(mode), Is.EqualTo(valid));
        }

        // ── Whose check, and which one counts ──

        [Test]
        public void Rider_key_is_the_riders_normalized_name_else_the_account_owners()
        {
            Assert.That(IdCheckRules.RiderKey("  Cody ", "KID", "Pat", "Parent"), Is.EqualTo("cody kid"));
            Assert.That(IdCheckRules.RiderKey(null, " ", "Pat", "Parent"), Is.EqualTo("pat parent"));
            // A parent's own record and their child's are different keys under one account.
            Assert.That(IdCheckRules.RiderKey("Pat", "Parent", "Pat", "Parent"),
                Is.Not.EqualTo(IdCheckRules.RiderKey("Cody", "Kid", "Pat", "Parent")));
        }

        [Test]
        public void Account_is_the_rider_only_by_name()
        {
            Assert.That(IdCheckRules.AccountIsRider(null, null, "Pat", "Parent"), Is.True);
            Assert.That(IdCheckRules.AccountIsRider("pat", "  parent", "Pat", "Parent"), Is.True);
            Assert.That(IdCheckRules.AccountIsRider("Cody", "Kid", "Pat", "Parent"), Is.False);
        }

        [Test]
        public void The_most_recent_check_wins_and_its_dob_is_the_evidence()
        {
            var older = new IdCheck(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), null,
                new DateTime(2000, 1, 1), IdVerificationScope.Rider);
            var newer = new IdCheck(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), null,
                new DateTime(2001, 2, 2), IdVerificationScope.Credential);
            Assert.That(IdCheckRules.Latest(older, newer), Is.SameAs(newer));
            Assert.That(IdCheckRules.Latest(newer, null, older), Is.SameAs(newer));
            Assert.That(IdCheckRules.Latest(null, null), Is.Null);
            Assert.That(IdCheckRules.Latest(older)!.VerifiedDob, Is.EqualTo(new DateTime(2000, 1, 1)));
        }
    }
}
