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
    }
}
