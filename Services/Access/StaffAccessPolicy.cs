using System.Net;
using Services.Repositories.Data.TenantData;

namespace Services.Access
{
    /// <summary>
    /// Decides whether a money-moving request is coming from somewhere and sometime the tenant
    /// allows. Pure logic on purpose: no HttpContext, no clock, no database, so every branch is
    /// reachable from a test with a plain Tenant, an IPAddress, and an instant.
    /// </summary>
    public static class StaffAccessPolicy
    {
        public enum Denial
        {
            None = 0,
            OffSite,
            OffHours,
        }

        /// <summary>
        /// Null when the request is allowed, otherwise why it is not.
        /// <paramref name="nowUtc"/> is converted to the tenant's own zone before the hours
        /// comparison, because "we close at 8" means 8 at the track.
        /// </summary>
        public static Denial Evaluate(Tenant tenant, IPAddress? clientIp, DateTime nowUtc)
        {
            // Mode 0 (the default every tenant starts on) means the whole feature is inert.
            if (tenant.StaffAccessPolicyMode != 1) return Denial.None;

            if (!IsAllowedLocation(tenant.StaffAllowedCidrs, clientIp)) return Denial.OffSite;
            if (!IsWithinHours(tenant, nowUtc)) return Denial.OffHours;
            return Denial.None;
        }

        /// <summary>Empty list = no location rule. A request we cannot attribute to an address is
        /// treated as off-site rather than waved through: failing open here would make the rule
        /// bypassable by whatever caused the address to go missing.</summary>
        public static bool IsAllowedLocation(string[]? allowedCidrs, IPAddress? clientIp)
        {
            if (allowedCidrs is null || allowedCidrs.Length == 0) return true;
            if (clientIp is null) return false;

            // An IPv4 client arriving over a dual-stack socket shows up as ::ffff:203.0.113.5 and
            // would never match a plain 203.0.113.0/24 rule, so unwrap it first.
            if (clientIp.IsIPv4MappedToIPv6) clientIp = clientIp.MapToIPv4();

            foreach (var entry in allowedCidrs)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                if (TryMatch(entry.Trim(), clientIp)) return true;
            }
            return false;
        }

        /// <summary>Accepts either a CIDR block ("203.0.113.0/24") or a bare address, which is
        /// treated as a single host. Anything unparseable is ignored rather than throwing: a
        /// typo in one entry must not take the gate offline, and the remaining entries still
        /// apply. The settings screen validates on the way in so typos are caught there.</summary>
        private static bool TryMatch(string entry, IPAddress clientIp)
        {
            if (entry.Contains('/'))
            {
                if (!IPNetwork.TryParse(entry, out var network)) return false;
                return network.Contains(clientIp);
            }
            return IPAddress.TryParse(entry, out var single) && single.Equals(clientIp);
        }

        /// <summary>Both hours null = no clock rule. A window whose end is at or before its start
        /// crosses midnight (22:00 to 02:00), which is how a night event actually runs.</summary>
        public static bool IsWithinHours(Tenant tenant, DateTime nowUtc)
        {
            if (tenant.StaffHoursStart is not TimeSpan start || tenant.StaffHoursEnd is not TimeSpan end)
                return true;

            TimeZoneInfo tz;
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById(tenant.Timezone);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // A tenant with an unusable timezone would otherwise have every staff action
                // denied by a rule they cannot see. Misconfiguration should not lock the gate.
                return true;
            }

            var local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), tz).TimeOfDay;

            // Same instant both sides means a zero-length window, which can only be a mistake;
            // read it as "no restriction" rather than "nothing is ever allowed".
            if (start == end) return true;
            return start < end
                ? local >= start && local < end          // ordinary daytime window
                : local >= start || local < end;         // crosses midnight
        }
    }
}
