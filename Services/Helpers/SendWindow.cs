namespace Services.Helpers
{
    /// <summary>
    /// "Only send between 9am and 6pm, track local." Lives here rather than inside the sweep so
    /// the midnight-wrap case is testable: a track that wants an overnight window (22:00 to 06:00)
    /// is asking for two ranges, and treating it as one silently sends at noon instead.
    /// </summary>
    public static class SendWindow
    {
        /// <summary>
        /// True when <paramref name="utcNow"/>, converted to the tenant's local time, falls inside
        /// [start, end). Both bounds null means any hour.
        /// </summary>
        public static bool IsOpen(TimeSpan? start, TimeSpan? end, string? timezone, DateTime utcNow)
        {
            if (start is not TimeSpan s || end is not TimeSpan e) return true;
            // Degenerate window: a tenant who set both bounds the same meant a window, not a
            // permanent block, and blocking forever is the failure they would never diagnose.
            if (s == e) return true;

            var local = ToLocal(utcNow, timezone);
            var now = local.TimeOfDay;
            return s < e
                ? now >= s && now < e            // 09:00 to 18:00
                : now >= s || now < e;           // 22:00 to 06:00, wrapping midnight
        }

        /// <summary>Tenant-local time, falling back to UTC when the zone id is missing or unknown.</summary>
        public static DateTime ToLocal(DateTime utcNow, string? timezone)
        {
            if (string.IsNullOrWhiteSpace(timezone)) return utcNow;
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.FindSystemTimeZoneById(timezone));
            }
            catch (TimeZoneNotFoundException) { return utcNow; }
            catch (InvalidTimeZoneException) { return utcNow; }
        }
    }
}
