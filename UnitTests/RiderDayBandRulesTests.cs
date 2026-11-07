using NUnit.Framework;
using Services.Repositories.Data.WristbandData;
using webapi.Helpers;

namespace UnitTests
{
    // Daily wristbands (Script0312): one band per rider per day, one rider per band per day.
    [TestFixture]
    public class RiderDayBandRulesTests
    {
        private static RiderDayBand Band(string code) => new() { Id = Guid.NewGuid(), Code = code };

        [Test]
        public void NoBandYet_FreeCode_Issues()
        {
            Assert.That(RiderDayBandRules.Decide(null, null, "0347", false), Is.EqualTo(DayBandAction.Issue));
            // A "replacement" for a rider with no band today is simply their band.
            Assert.That(RiderDayBandRules.Decide(null, null, "0347", true), Is.EqualTo(DayBandAction.Issue));
        }

        [Test]
        public void SameBandAgain_IsAlreadyWorn_CaseInsensitive()
        {
            var mine = Band("AbC-1");
            Assert.That(RiderDayBandRules.Decide(mine, mine, "abc-1", false), Is.EqualTo(DayBandAction.AlreadyWorn));
        }

        [Test]
        public void ReplacingWithTheBandAlreadyWorn_IsItsOwnRefusal()
        {
            var mine = Band("AbC-1");
            Assert.That(RiderDayBandRules.Decide(mine, mine, "ABC-1", true), Is.EqualTo(DayBandAction.ReplaceWithSameBand));
        }

        [Test]
        public void SecondBandForTheSameRider_RefusedUnlessReplacing()
        {
            var mine = Band("0347");
            Assert.That(RiderDayBandRules.Decide(mine, null, "0348", false), Is.EqualTo(DayBandAction.RiderHasBand));
            Assert.That(RiderDayBandRules.Decide(mine, null, "0348", true), Is.EqualTo(DayBandAction.Replace));
        }

        [Test]
        public void CodeOnSomeoneElse_RefusedEvenWhenReplacing()
        {
            var theirs = Band("0347");
            Assert.That(RiderDayBandRules.Decide(null, theirs, "0347", false), Is.EqualTo(DayBandAction.CodeTaken));
            Assert.That(RiderDayBandRules.Decide(Band("0001"), theirs, "0347", true), Is.EqualTo(DayBandAction.CodeTaken));
        }

        [Test]
        public void CleanCode_TrimsAndRejectsEmptyOrHuge()
        {
            Assert.That(RiderDayBandRules.CleanCode("  0347 "), Is.EqualTo("0347"));
            Assert.That(RiderDayBandRules.CleanCode("   "), Is.Null);
            Assert.That(RiderDayBandRules.CleanCode(null), Is.Null);
            Assert.That(RiderDayBandRules.CleanCode(new string('x', RiderDayBandRules.MaxCodeLength + 1)), Is.Null);
        }

        [Test]
        public void TicketDate_SingleDayIsTheEventsDate_CampIsEachDay()
        {
            var d = new DateOnly(2026, 7, 4);
            // A single-day event: its own date (even checked in the evening before).
            Assert.That(RiderDayBandRules.TicketDate(d, d, d.AddDays(-1)), Is.EqualTo(d));
            // A three-day camp: each day is that day's band; outside it, its nearest day.
            Assert.That(RiderDayBandRules.TicketDate(d, d.AddDays(2), d.AddDays(1)), Is.EqualTo(d.AddDays(1)));
            Assert.That(RiderDayBandRules.TicketDate(d, d.AddDays(2), d.AddDays(-1)), Is.EqualTo(d));
            Assert.That(RiderDayBandRules.TicketDate(d, d.AddDays(2), d.AddDays(5)), Is.EqualTo(d.AddDays(2)));
        }

        [Test]
        public void BusinessDate_IsTheTenantsLocalDate()
        {
            // 03:00 UTC on the 16th is still the 15th in Denver: a band issued then is the 15th's band.
            var utc = new DateTime(2026, 6, 16, 3, 0, 0, DateTimeKind.Utc);
            Assert.That(Services.Helpers.TenantClock.LocalDate("America/Denver", utc), Is.EqualTo(new DateOnly(2026, 6, 15)));
        }
    }
}
