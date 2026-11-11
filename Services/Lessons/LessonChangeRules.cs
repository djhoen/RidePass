namespace Services.Lessons
{
    /// <summary>
    /// When a rider may move a lesson booking to another date (Script0319). Pure, so the window rules
    /// are unit-tested on their own. Staff bypass all of this; the venue cancelling for weather lifts
    /// the cutoff for its riders.
    /// </summary>
    public static class LessonChangeRules
    {
        public const string LessonTypeCode = "lesson";

        /// <summary>Why a rider can't change this booking right now, or null when they can.</summary>
        public static string? RiderBlockReason(DateTime nowUtc, DateTime sessionStartUtc, int cutoffHours, bool weatherCancelled)
        {
            if (weatherCancelled) return null;
            if (sessionStartUtc <= nowUtc) return "This lesson has already started.";
            if (sessionStartUtc - nowUtc < TimeSpan.FromHours(cutoffHours))
                return cutoffHours == 0
                    ? "This lesson can't be changed any more."
                    : $"Lessons can only be moved up to {cutoffHours} hours before they start. Contact the venue if something has come up.";
            return null;
        }

        /// <summary>
        /// Whether a session can be moved onto: it hasn't started, isn't today in the venue's time
        /// (no same-day changes), and, outside a weather cancellation, is itself beyond the cutoff.
        /// </summary>
        public static bool IsAllowedTarget(DateTime nowUtc, DateTime targetStartUtc, TimeZoneInfo venueZone,
            int cutoffHours, bool weatherCancelled)
        {
            if (targetStartUtc <= nowUtc) return false;
            var today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, venueZone).Date;
            var targetDay = TimeZoneInfo.ConvertTimeFromUtc(targetStartUtc, venueZone).Date;
            if (targetDay <= today) return false;
            if (weatherCancelled) return true;
            return targetStartUtc - nowUtc >= TimeSpan.FromHours(cutoffHours);
        }

        /// <summary>Two lesson groups are "the same lesson" when their names match, ignoring case and spacing.</summary>
        public static bool SameGroup(string a, string b) => Normalize(a) == Normalize(b);

        public static string Normalize(string s) =>
            string.Join(' ', (s ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
