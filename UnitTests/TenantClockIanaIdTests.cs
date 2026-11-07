using NUnit.Framework;
using Services.Helpers;

namespace UnitTests
{
    // Every tenant zone that reaches SQL (Postgres AT TIME ZONE) is an IANA name, whatever the setting holds.
    [TestFixture]
    public class TenantClockIanaIdTests
    {
        [TestCase("America/Santiago", "America/Santiago")]
        [TestCase("America/New_York", "America/New_York")]
        [TestCase("Eastern Standard Time", "America/New_York")]   // Windows-style id
        [TestCase("Pacific SA Standard Time", "America/Santiago")]
        public void A_known_zone_resolves_to_its_IANA_name(string setting, string expected) =>
            Assert.That(TenantClock.IanaId(setting), Is.EqualTo(expected));

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Not/AZone")]
        public void An_empty_or_unknown_setting_falls_back_to_UTC(string? setting) =>
            Assert.That(TenantClock.IanaId(setting), Is.EqualTo("Etc/UTC").Or.EqualTo("UTC"));
    }
}
