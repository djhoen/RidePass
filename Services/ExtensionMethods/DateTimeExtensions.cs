namespace Services.ExtensionMethods
{
    public static class DateTimeExtensions
    {
        public static DateTime ToLocalDate(this DateTime dt)
        {
            try
            {
                bool isDaylight = TimeZoneInfo.Local.IsDaylightSavingTime(dt);
                return dt.AddHours(isDaylight ? -6 : -7);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public static DateTime ToServerDate(this DateTime dt)
        {
            try
            {
                bool isDaylight = TimeZoneInfo.Local.IsDaylightSavingTime(dt);
                return dt.AddHours(isDaylight ? 6 : 7);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public static DateTime AddBusinessDays(this DateTime date, int days)
        {
            if (days < 0)
            {
                throw new ArgumentException("days cannot be negative", "days");
            }

            if (days == 0) return date;

            if (date.DayOfWeek == DayOfWeek.Saturday)
            {
                date = date.AddDays(2);
                days -= 1;
            }
            else if (date.DayOfWeek == DayOfWeek.Sunday)
            {
                date = date.AddDays(1);
                days -= 1;
            }

            date = date.AddDays(days / 5 * 7);
            int extraDays = days % 5;

            if ((int)date.DayOfWeek + extraDays > 5)
            {
                extraDays += 2;
            }

            return date.AddDays(extraDays);
        }

        public static bool IsWeekend(this DateTime value)
        {
            return (value.DayOfWeek == DayOfWeek.Sunday || value.DayOfWeek == DayOfWeek.Saturday);
        }

        public static DateTime StartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
        {
            int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
            return dt.AddDays(-1 * diff).Date;
        }

        public static string AsTimeAgo(this DateTime dateTime)
        {
            TimeSpan timeSpan = DateTime.Now.Subtract(dateTime);

            return timeSpan.TotalSeconds switch
            {
                <= 60 => $"{timeSpan.Seconds} seconds ago",
                _ => timeSpan.TotalMinutes switch
                {
                    <= 1 => "about a minute ago",
                    < 60 => $"about {timeSpan.Minutes} minutes ago",
                    _ => timeSpan.TotalHours switch
                    {
                        <= 1 => "about an hour ago",
                        < 24 => $"about {timeSpan.Hours} hours ago",
                        _ => timeSpan.TotalDays switch
                        {
                            <= 1 => "yesterday",
                            <= 30 => $"about {timeSpan.Days} days ago",
                            <= 60 => "about a month ago",
                            < 365 => $"about {timeSpan.Days / 30} months ago",
                            <= 365 * 2 => "about a year ago",
                            _ => $"about {timeSpan.Days / 365} years ago"
                        }
                    }
                }
            };
        }
    }
}
