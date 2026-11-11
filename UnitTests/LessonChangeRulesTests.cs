using NUnit.Framework;
using Services.Helpers;
using Services.Lessons;

namespace UnitTests
{
    [TestFixture]
    public class LessonChangeRulesTests
    {
        private static readonly TimeZoneInfo NewYork = TenantClock.Resolve("America/New_York");
        // Tue Nov 10 2026, 10:00 New York (15:00 UTC).
        private static readonly DateTime Now = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Rider_can_change_more_than_cutoff_before()
            => Assert.That(LessonChangeRules.RiderBlockReason(Now, Now.AddHours(73), 72, false), Is.Null);

        [Test]
        public void Rider_cannot_change_inside_cutoff()
            => Assert.That(LessonChangeRules.RiderBlockReason(Now, Now.AddHours(71), 72, false), Does.Contain("72 hours"));

        [Test]
        public void Exactly_at_cutoff_is_still_allowed()
            => Assert.That(LessonChangeRules.RiderBlockReason(Now, Now.AddHours(72), 72, false), Is.Null);

        [Test]
        public void Started_lesson_cannot_change()
            => Assert.That(LessonChangeRules.RiderBlockReason(Now, Now.AddMinutes(-5), 72, false), Is.Not.Null);

        [Test]
        public void Weather_lifts_the_cutoff()
            => Assert.That(LessonChangeRules.RiderBlockReason(Now, Now.AddHours(2), 72, true), Is.Null);

        [Test]
        public void Target_must_be_beyond_cutoff_normally()
        {
            Assert.That(LessonChangeRules.IsAllowedTarget(Now, Now.AddHours(48), NewYork, 72, false), Is.False);
            Assert.That(LessonChangeRules.IsAllowedTarget(Now, Now.AddHours(96), NewYork, 72, false), Is.True);
        }

        [Test]
        public void Weather_target_can_be_tomorrow_but_not_today()
        {
            // 20:00 New York today: same local day.
            Assert.That(LessonChangeRules.IsAllowedTarget(Now, Now.AddHours(10), NewYork, 72, true), Is.False);
            // 09:00 New York tomorrow.
            Assert.That(LessonChangeRules.IsAllowedTarget(Now, Now.AddHours(23), NewYork, 72, true), Is.True);
        }

        [Test]
        public void Same_group_ignores_case_and_spacing()
        {
            Assert.That(LessonChangeRules.SameGroup("Beginner  Group", " beginner group"), Is.True);
            Assert.That(LessonChangeRules.SameGroup("Beginner", "Intermediate"), Is.False);
        }
    }
}
