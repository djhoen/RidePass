namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>One QuickBooks Desktop export file, for the export history table.</summary>
    public class QbdExportBatchResponse
    {
        public Guid Id { get; set; }
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        public int DayCount { get; set; }
        public long TotalDebitsCents { get; set; }
        public int DownloadCount { get; set; }
        public DateTime? LastDownloadedAtUtc { get; set; }
        public DateTime? VoidedAtUtc { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
