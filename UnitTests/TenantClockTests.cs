using NUnit.Framework;
using Services.Helpers;

namespace UnitTests
{
    // Tenant-local midnight as a UTC instant, the window every "today at the track" figure opens on.
    [TestFixture]
    public class TenantClockTests
    {
        [Test]
        public void BusinessDayStart_IsLocalMidnightInUtc()
        {
            // 2026-07-16 02:30 UTC is still the evening of July 15 in Denver (UTC-6 in summer), so
            // the business day began at July 15 00:00 MDT = July 15 06:00 UTC.
            var start = TenantClock.BusinessDayStartUtc("America/Denver", new DateTime(2026, 7, 16, 2, 30, 0, DateTimeKind.Utc));
            Assert.That(start, Is.EqualTo(new DateTime(2026, 7, 15, 6, 0, 0, DateTimeKind.Utc)));
            Assert.That(start.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Not/AZone")]
        public void UnknownOrMissingZone_ReadsAsUtc(string? zone)
        {
            var now = new DateTime(2026, 7, 16, 2, 30, 0, DateTimeKind.Utc);
            Assert.That(TenantClock.Resolve(zone), Is.EqualTo(TimeZoneInfo.Utc));
            Assert.That(TenantClock.BusinessDayStartUtc(zone, now), Is.EqualTo(new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void DayRange_IsLocalMidnightToNextLocalMidnight_InUtc()
        {
            // A July Saturday in New York (EDT, UTC-4): 00:00 local is 04:00Z, and the day ends at
            // the next 04:00Z, not 24 hours after whatever "now" is.
            var (start, end) = TenantClock.DayRangeUtc("America/New_York", new DateOnly(2026, 7, 11));
            Assert.That(start, Is.EqualTo(new DateTime(2026, 7, 11, 4, 0, 0, DateTimeKind.Utc)));
            Assert.That(end, Is.EqualTo(new DateTime(2026, 7, 12, 4, 0, 0, DateTimeKind.Utc)));
            Assert.That(start.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(end.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void DayRange_FallBackDay_IsTwentyFiveHours()
        {
            // 2026-11-01 is when New York leaves DST: the day starts at 04:00Z (EDT) and the next
            // one at 05:00Z (EST), so the business day really is 25 hours long.
            var (start, end) = TenantClock.DayRangeUtc("America/New_York", new DateOnly(2026, 11, 1));
            Assert.That(start, Is.EqualTo(new DateTime(2026, 11, 1, 4, 0, 0, DateTimeKind.Utc)));
            Assert.That(end, Is.EqualTo(new DateTime(2026, 11, 2, 5, 0, 0, DateTimeKind.Utc)));
            Assert.That(end - start, Is.EqualTo(TimeSpan.FromHours(25)));
        }

        [Test]
        public void DayRange_SpringForwardDay_IsTwentyThreeHours()
        {
            var (start, end) = TenantClock.DayRangeUtc("America/New_York", new DateOnly(2026, 3, 8));
            Assert.That(end - start, Is.EqualTo(TimeSpan.FromHours(23)));
        }

        [Test]
        public void DayRange_UnknownZone_ReadsAsUtc()
        {
            var (start, end) = TenantClock.DayRangeUtc("Not/AZone", new DateOnly(2026, 7, 11));
            Assert.That(start, Is.EqualTo(new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(end, Is.EqualTo(new DateTime(2026, 7, 12, 0, 0, 0, DateTimeKind.Utc)));
        }
    }
}
