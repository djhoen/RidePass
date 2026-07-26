using NUnit.Framework;

namespace UnitTests
{
    // The check-in list's date window. A wrong bound doesn't error, it silently hides arrivals, so
    // the tenant-timezone conversion is worth pinning down. Mirrors ExtraController.TenantDayBound.
    [TestFixture]
    public class AddOnCheckInWindowTests
    {
        private static DateTime? Bound(string? date, bool endOfDay, string? tzId)
        {
            if (!DateOnly.TryParse(date, out var d)) return null;
            var local = endOfDay ? d.ToDateTime(new TimeOnly(23, 59, 59)) : d.ToDateTime(TimeOnly.MinValue);
            if (string.IsNullOrWhiteSpace(tzId)) return DateTime.SpecifyKind(local, DateTimeKind.Utc);
            try
            {
                return TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                    TimeZoneInfo.FindSystemTimeZoneById(tzId));
            }
            catch (TimeZoneNotFoundException) { return DateTime.SpecifyKind(local, DateTimeKind.Utc); }
        }

        // July is MDT (UTC-6), so a Denver day starts at 06:00 UTC and ends just before 06:00 the
        // next day. Read as server-local on a UTC box, the window would end at 23:59 the same day
        // and lose the last six hours of arrivals.
        [Test]
        public void DenverDayIsShiftedIntoUtc()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Bound("2026-07-26", false, "America/Denver"),
                    Is.EqualTo(new DateTime(2026, 7, 26, 6, 0, 0, DateTimeKind.Utc)));
                Assert.That(Bound("2026-07-26", true, "America/Denver"),
                    Is.EqualTo(new DateTime(2026, 7, 27, 5, 59, 59, DateTimeKind.Utc)));
            });
        }

        // Standard time is UTC-7, so the same call in January lands an hour later.
        [Test]
        public void WinterOffsetDiffersFromSummer()
        {
            Assert.That(Bound("2026-01-15", false, "America/Denver"),
                Is.EqualTo(new DateTime(2026, 1, 15, 7, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void UtcTenantIsUnshifted()
        {
            Assert.That(Bound("2026-07-26", false, "UTC"),
                Is.EqualTo(new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc)));
        }

        // A typo in a date box must not refuse the whole list; no bound is the safe direction,
        // because showing more rows than asked for beats showing none.
        [Test]
        public void UnparseableOrMissingDateMeansNoBound()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Bound("not-a-date", false, "America/Denver"), Is.Null);
                Assert.That(Bound(null, false, "America/Denver"), Is.Null);
                Assert.That(Bound("", true, "America/Denver"), Is.Null);
            });
        }

        [Test]
        public void UnknownTimezoneFallsBackToUtcRatherThanThrowing()
        {
            Assert.That(Bound("2026-07-26", false, "Mars/Olympus_Mons"),
                Is.EqualTo(new DateTime(2026, 7, 26, 0, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void EndBoundIsAfterStartBound()
        {
            var from = Bound("2026-07-26", false, "America/Denver");
            var to = Bound("2026-07-26", true, "America/Denver");
            Assert.That(to, Is.GreaterThan(from));
        }
    }
}
