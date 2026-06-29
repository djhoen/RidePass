namespace webapi.Controllers.API.Data.Concession
{
    // Live online-ordering status the rider app polls: whether ordering is open, the estimated ready
    // time, and (when closed) why. QuoteMinutes is null when quotes are off or ordering is closed.
    public class ConcessionOrderingStatusResponse
    {
        public bool OpenNow { get; set; }
        public int? QuoteMinutes { get; set; }
        public bool CapReached { get; set; }
        public bool PausedManual { get; set; }
        // Whether the throttle feature is on at all (so staff screens know to show the pause control).
        public bool CapacityEnabled { get; set; }
        public string? Reason { get; set; }
    }
}
