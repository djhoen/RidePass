namespace Services.Repositories.Data.QuickBooksData
{
    /// <summary>
    /// The outcome of posting one business date for one tenant (qbo_sync_log). The unique index on
    /// (tenant_id, business_date) is the idempotency anchor for the whole sync: it is what makes a
    /// retry, a manual re-sync, or two dispatchers racing unable to post the same day twice.
    /// </summary>
    public class QboSyncLogEntry
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public DateOnly BusinessDate { get; set; }
        /// <summary>success | failed | no_activity</summary>
        public string Status { get; set; } = null!;
        /// <summary>The QBO JournalEntry.Id. Set on success; its presence means "already posted".</summary>
        public string? QboJournalEntryId { get; set; }
        public string? QboDocNumber { get; set; }
        /// <summary>How many accounting rows were summarised into the post.</summary>
        public int EntryCount { get; set; }
        /// <summary>Equals total credits by construction, a balance tripwire visible without opening QBO.</summary>
        public long TotalDebitsCents { get; set; }
        public int AttemptCount { get; set; }
        public string? LastError { get; set; }
        public DateTime? SyncedAtUtc { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
