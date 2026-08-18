namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// Where this business date stands with QuickBooks, so the close and the books read as one
    /// screen. Read-only: nothing on the End of Day path posts, retries, or claims a date.
    /// </summary>
    public class EndOfDayQuickBooksStatus
    {
        /// <summary>The tenant has a QuickBooks connection row.</summary>
        public bool Connected { get; set; }
        /// <summary>not_connected | disabled | pending | success | failed | no_activity</summary>
        public string Status { get; set; } = "not_connected";
        /// <summary>The deterministic RP-yyyyMMdd document number, once posted.</summary>
        public string? DocNumber { get; set; }
        public string? JournalEntryId { get; set; }
        public DateTime? SyncedAtUtc { get; set; }
        public string? LastError { get; set; }
    }
}
