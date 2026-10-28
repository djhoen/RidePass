namespace Services.Repositories.Data.QuickBooksData
{
    /// <summary>
    /// One QuickBooks Desktop export (qbd_export_batch): the IIF file as first generated plus the
    /// closed business days it covers. Re-downloading always returns <see cref="FileContent"/>
    /// unchanged; voiding releases the days for the next export.
    /// </summary>
    public class QbdExportBatch
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public DateOnly FromDate { get; set; }
        public DateOnly ToDate { get; set; }
        /// <summary>Days with a journal entry in the file (days with no activity are not counted).</summary>
        public int DayCount { get; set; }
        public long TotalDebitsCents { get; set; }
        public string FileContent { get; set; } = null!;
        public Guid? CreatedByUserId { get; set; }
        public int DownloadCount { get; set; }
        public DateTime? LastDownloadedAtUtc { get; set; }
        public DateTime? VoidedAtUtc { get; set; }
        public Guid? VoidedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>One day going into a new batch, already built and validated.</summary>
    public sealed record QbdExportDay(DateOnly BusinessDate, string Status, int EntryCount, long TotalDebitsCents);
}
