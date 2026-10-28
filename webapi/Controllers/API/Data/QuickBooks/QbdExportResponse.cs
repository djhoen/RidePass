namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>Outcome of building a QuickBooks Desktop export.</summary>
    public class QbdExportResponse
    {
        /// <summary>Null when there was nothing new to export.</summary>
        public Guid? BatchId { get; set; }
        public int DaysInFile { get; set; }
        public int DaysWithNoActivity { get; set; }
        public DateOnly? FromDate { get; set; }
        public DateOnly? ToDate { get; set; }
        /// <summary>The newest day that has closed at the track; nothing after it can be exported yet.</summary>
        public DateOnly LastCompleteDay { get; set; }
        /// <summary>More closed days are waiting than one file holds; export again for the rest.</summary>
        public bool MoreRemaining { get; set; }
    }
}
