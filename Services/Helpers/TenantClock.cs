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
    }
}
