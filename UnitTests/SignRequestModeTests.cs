using NUnit.Framework;
using Services.Waivers;
using webapi.Helpers;

namespace UnitTests
{
    // How a targeted waiver is signed (a signing link, or a waiver pushed to a kiosk): one rule for both.
    [TestFixture]
    public class SignRequestModeTests
    {
        private static WaiverSignTarget Target(bool riderAudience, bool? isMinor, DateOnly? birthdate, string? first = "Mia", string? last = "Minor") =>
            new()
            {
                FirstName = first, LastName = last, DisplayName = "Mia Minor",
                Decision = new WaiverGateDecision { RiderAudience = riderAudience, IsMinor = isMinor, Birthdate = birthdate },
            };

        [Test]
        public void Rider_TheGatesMinorVerdictDecides()
        {
            Assert.That(SignRequestMode.ForTarget(Target(true, true, new DateOnly(2014, 1, 1))).SignAs, Is.EqualTo("guardian"));
            Assert.That(SignRequestMode.ForTarget(Target(true, false, new DateOnly(1990, 1, 1))).SignAs, Is.EqualTo("adult"));
        }

        [Test]
        public void Spectator_TheBirthdateDecides_UnknownMeansAskForIt()
        {
            Assert.That(SignRequestMode.ForTarget(Target(false, null, DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10)))).SignAs, Is.EqualTo("guardian"));
            Assert.That(SignRequestMode.ForTarget(Target(false, null, null)).SignAs, Is.Null);
        }

        [Test]
        public void TheRiderIsNamedFromTheTarget_BlankIsUnknown()
        {
            var m = SignRequestMode.ForTarget(Target(true, true, new DateOnly(2014, 1, 1), " Mia ", " "));
            Assert.That(m.RiderFirstName, Is.EqualTo("Mia"));
            Assert.That(m.RiderLastName, Is.Null);
            Assert.That(m.RiderBirthdate, Is.EqualTo(new DateTime(2014, 1, 1)));
        }
    }
}
