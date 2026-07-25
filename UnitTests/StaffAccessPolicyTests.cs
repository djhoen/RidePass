using System.Net;
using NUnit.Framework;
using Services.Access;
using Services.Repositories.Data.TenantData;

namespace UnitTests
{
    /// <summary>
    /// The staff access policy decides whether a refund, a counter sale, or a gate scan is allowed
    /// from a given address at a given moment. Getting it wrong in one direction locks a track out
    /// of its own register mid-event; in the other it leaves the control doing nothing. The edge
    /// cases below (midnight-crossing windows, IPv4-mapped addresses, unusable timezones) are the
    /// ones that are easy to reason about incorrectly.
    /// </summary>
    [TestFixture]
    public class StaffAccessPolicyTests
    {
        private static Tenant Tenant(
            int mode = 1,
            string[]? cidrs = null,
            string? start = null,
            string? end = null,
            string tz = "America/Denver") => new()
            {
                StaffAccessPolicyMode = mode,
                StaffAllowedCidrs = cidrs ?? System.Array.Empty<string>(),
                StaffHoursStart = start is null ? null : TimeSpan.Parse(start),
                StaffHoursEnd = end is null ? null : TimeSpan.Parse(end),
                Timezone = tz,
            };

        private static IPAddress Ip(string s) => IPAddress.Parse(s);

        // 19:00 UTC = 13:00 in Denver (MDT, UTC-6) on this date.
        private static readonly DateTime MiddayUtc = new(2026, 7, 25, 19, 0, 0, DateTimeKind.Utc);
        // 08:00 UTC = 02:00 in Denver.
        private static readonly DateTime PredawnUtc = new(2026, 7, 25, 8, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Mode_off_allows_everything_even_from_an_unlisted_address()
        {
            var t = Tenant(mode: 0, cidrs: new[] { "203.0.113.0/24" }, start: "06:00", end: "20:00");
            Assert.That(StaffAccessPolicy.Evaluate(t, Ip("24.9.100.7"), PredawnUtc),
                Is.EqualTo(StaffAccessPolicy.Denial.None),
                "Mode 0 is the default every tenant starts on; it must change nothing.");
        }

        [Test]
        public void No_constraints_configured_allows_everything()
        {
            var t = Tenant();
            Assert.That(StaffAccessPolicy.Evaluate(t, Ip("24.9.100.7"), PredawnUtc),
                Is.EqualTo(StaffAccessPolicy.Denial.None));
        }

        [Test]
        public void Address_inside_an_allowed_block_is_permitted()
        {
            var t = Tenant(cidrs: new[] { "203.0.113.0/24" });
            Assert.That(StaffAccessPolicy.Evaluate(t, Ip("203.0.113.45"), MiddayUtc),
                Is.EqualTo(StaffAccessPolicy.Denial.None));
        }

        [Test]
        public void Address_outside_every_allowed_block_is_off_site()
        {
            var t = Tenant(cidrs: new[] { "203.0.113.0/24" });
            Assert.That(StaffAccessPolicy.Evaluate(t, Ip("24.9.100.7"), MiddayUtc),
                Is.EqualTo(StaffAccessPolicy.Denial.OffSite),
                "A residential address outside the track's network is the whole point of the rule.");
        }

        [Test]
        public void A_bare_address_entry_matches_only_that_host()
        {
            var t = Tenant(cidrs: new[] { "203.0.113.45" });
            Assert.Multiple(() =>
            {
                Assert.That(StaffAccessPolicy.IsAllowedLocation(t.StaffAllowedCidrs, Ip("203.0.113.45")), Is.True);
                Assert.That(StaffAccessPolicy.IsAllowedLocation(t.StaffAllowedCidrs, Ip("203.0.113.46")), Is.False);
            });
        }

        [Test]
        public void An_ipv4_client_arriving_mapped_into_ipv6_still_matches_an_ipv4_rule()
        {
            // Dual-stack sockets present 203.0.113.45 as ::ffff:203.0.113.45. Without unwrapping,
            // a track would allowlist its own network and then be locked out by it.
            var mapped = Ip("203.0.113.45").MapToIPv6();
            Assert.That(mapped.IsIPv4MappedToIPv6, Is.True, "guard: the fixture must actually be mapped");
            Assert.That(StaffAccessPolicy.IsAllowedLocation(new[] { "203.0.113.0/24" }, mapped), Is.True);
        }

        [Test]
        public void A_missing_client_address_is_treated_as_off_site()
        {
            // Fail closed: whatever caused the address to go missing must not become a bypass.
            Assert.That(StaffAccessPolicy.IsAllowedLocation(new[] { "203.0.113.0/24" }, null), Is.False);
        }

        [Test]
        public void An_unparseable_entry_is_ignored_without_taking_the_rest_down()
        {
            var cidrs = new[] { "not-an-address", "203.0.113.0/24" };
            Assert.Multiple(() =>
            {
                Assert.That(StaffAccessPolicy.IsAllowedLocation(cidrs, Ip("203.0.113.45")), Is.True,
                    "One typo must not disable the entries that are valid.");
                Assert.That(StaffAccessPolicy.IsAllowedLocation(cidrs, Ip("24.9.100.7")), Is.False,
                    "...and must not accidentally match everything either.");
            });
        }

        [Test]
        public void Inside_an_ordinary_daytime_window_is_allowed()
        {
            var t = Tenant(start: "06:00", end: "20:00");   // 13:00 local
            Assert.That(StaffAccessPolicy.Evaluate(t, Ip("24.9.100.7"), MiddayUtc),
                Is.EqualTo(StaffAccessPolicy.Denial.None));
        }

        [Test]
        public void Outside_an_ordinary_daytime_window_is_off_hours()
        {
            var t = Tenant(start: "06:00", end: "20:00");   // 02:00 local
            Assert.That(StaffAccessPolicy.Evaluate(t, Ip("24.9.100.7"), PredawnUtc),
                Is.EqualTo(StaffAccessPolicy.Denial.OffHours));
        }

        [Test]
        public void A_window_crossing_midnight_covers_the_small_hours()
        {
            // A night event running 22:00 to 04:00 must not be read as an empty window.
            var t = Tenant(start: "22:00", end: "04:00");
            Assert.Multiple(() =>
            {
                Assert.That(StaffAccessPolicy.IsWithinHours(t, PredawnUtc), Is.True, "02:00 local is inside 22:00-04:00");
                Assert.That(StaffAccessPolicy.IsWithinHours(t, MiddayUtc), Is.False, "13:00 local is outside it");
            });
        }

        [Test]
        public void A_zero_length_window_is_read_as_no_restriction()
        {
            var t = Tenant(start: "09:00", end: "09:00");
            Assert.That(StaffAccessPolicy.IsWithinHours(t, PredawnUtc), Is.True,
                "start == end can only be a mistake; it must not mean 'never allowed'.");
        }

        [Test]
        public void Hours_are_evaluated_in_the_tenants_zone_not_utc()
        {
            // 19:00 UTC is inside a 18:00-23:00 UTC window but outside 18:00-23:00 in Denver,
            // where it is 13:00. If this fails, the comparison is running against the wrong clock.
            var t = Tenant(start: "18:00", end: "23:00");
            Assert.That(StaffAccessPolicy.IsWithinHours(t, MiddayUtc), Is.False);
        }

        [Test]
        public void An_unusable_timezone_does_not_lock_the_gate()
        {
            var t = Tenant(start: "06:00", end: "20:00", tz: "Not/AZone");
            Assert.That(StaffAccessPolicy.IsWithinHours(t, PredawnUtc), Is.True,
                "A misconfigured tenant must not have every staff action denied by an invisible rule.");
        }

        [Test]
        public void Location_is_reported_before_hours_when_both_fail()
        {
            var t = Tenant(cidrs: new[] { "203.0.113.0/24" }, start: "06:00", end: "20:00");
            Assert.That(StaffAccessPolicy.Evaluate(t, Ip("24.9.100.7"), PredawnUtc),
                Is.EqualTo(StaffAccessPolicy.Denial.OffSite),
                "Staff-facing copy should name the address problem first; it is the actionable one.");
        }
    }
}
