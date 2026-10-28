namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>Switch a track onto QuickBooks Desktop, or move its export start date.</summary>
    public class QbdSetupRequest
    {
        /// <summary>First business date to export. Omit for today at the track.</summary>
        public DateOnly? SyncStartDate { get; set; }
    }
}
