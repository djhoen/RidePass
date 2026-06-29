namespace webapi.Controllers.API.Data.Concession
{
    // One day's online-ordering window. Minutes are from local midnight (0-1440) in the tenant timezone.
    // Open = false means closed that day.
    public class ConcessionOrderingHoursDay
    {
        public bool Open { get; set; }
        public int OpenMinute { get; set; }
        public int CloseMinute { get; set; }
    }
}
