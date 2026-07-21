namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>One posted (or attempted) business date, for the sync history table.</summary>
    public class QboSyncLogResponse
    {
        public DateOnly BusinessDate { get; set; }
        /// <summary>success | failed | no_activity</summary>
        public string Status { get; set; } = null!;
        public string? QboJournalEntryId { get; set; }
        public string? QboDocNumber { get; set; }
        public int EntryCount { get; set; }
        public long TotalDebitsCents { get; set; }
        public int AttemptCount { get; set; }
        public string? LastError { get; set; }
        public DateTime? SyncedAtUtc { get; set; }
    }
}
