namespace Services.Helpers
{
    /// <summary>
    /// Tenant-local time, resolved from the IANA zone stored on the tenant. A missing, blank or
    /// unknown zone reads as UTC rather than failing the request: a mis-typed zone should make a
    /// report a few hours off, not take the screen down.
    /// </summary>
    public static class TenantClock
    {
        /// <summary>
        /// A database instant as an explicit UTC DateTime for a response. Npgsql hands timestamptz
        /// back as Kind=Utc already, so this is a no-op stamp in practice; it exists so every DTO
        /// serialises with a trailing Z instead of depending on how a value was read.
        /// </summary>
        public static DateTime AsUtc(DateTime d) => DateTime.SpecifyKind(d.ToUniversalTime(), DateTimeKind.Utc);

        public static TimeZoneInfo Resolve(string? timezoneId)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timezoneId) ? "UTC" : timezoneId); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
        }

        /// <summary>
        /// Midnight at the start of the tenant's current business day, as a UTC instant. "Today" is
        /// whichever local date <paramref name="nowUtc"/> falls on at the track.
        /// </summary>
        public static DateTime BusinessDayStartUtc(string? timezoneId, DateTime nowUtc)
        {
            var tz = Resolve(timezoneId);
            var localMidnight = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz).Date;
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), tz);
        }

        /// <summary>The tenant's local calendar date at <paramref name="nowUtc"/>: the business date a shift closes on.</summary>
        public static DateOnly LocalDate(string? timezoneId, DateTime nowUtc) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, Resolve(timezoneId)));

        /// <summary>
        /// The tenant-local calendar day <paramref name="day"/> as a half-open UTC window
        /// [StartUtc, EndUtc): local midnight to the next local midnight. Computed from both
        /// midnights rather than start + 24h so a DST day is its real 23 or 25 hours.
        /// </summary>
        public static (DateTime StartUtc, DateTime EndUtc) DayRangeUtc(string? timezoneId, DateOnly day)
        {
            var tz = Resolve(timezoneId);
            return (LocalMidnightUtc(tz, day), LocalMidnightUtc(tz, day.AddDays(1)));
        }

        private static DateTime LocalMidnightUtc(TimeZoneInfo tz, DateOnly day)
        {
            var local = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            // A few zones spring forward at midnight, so 00:00 does not exist on that day; the day
            // then begins at the first instant that does.
            if (tz.IsInvalidTime(local)) local = local.AddHours(1);
            return TimeZoneInfo.ConvertTimeToUtc(local, tz);
        }
    }
}
