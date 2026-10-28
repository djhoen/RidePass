namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>Everything the settings screen needs to render the QuickBooks panel in one call.</summary>
    public class QuickBooksStatusResponse
    {
        /// <summary>False when the Intuit app credentials aren't configured on this deployment at all (Online only).</summary>
        public bool IsConfigured { get; set; }
        /// <summary>online | desktop | null when the track uses neither yet.</summary>
        public string? Edition { get; set; }
        /// <summary>True when the track is set up on either edition.</summary>
        public bool IsConnected { get; set; }
        /// <summary>active | expired | revoked | error</summary>
        public string? Status { get; set; }
        public string? RealmId { get; set; }
        /// <summary>Resolved live from QBO when the link is healthy, so the tenant can confirm the right company.</summary>
        public string? CompanyName { get; set; }
        public bool SyncEnabled { get; set; }
        public DateOnly? SyncStartDate { get; set; }
        public DateOnly? LastSyncedDate { get; set; }
        public DateTime? LastSyncAtUtc { get; set; }
        public string? LastSyncError { get; set; }
        public DateTime? ConnectedAtUtc { get; set; }
        /// <summary>True once every account slot the tenant's activity can touch is mapped.</summary>
        public bool MappingComplete { get; set; }
        /// <summary>Account keys still needing an account. Drives the "finish setup" prompt.</summary>
        public List<string> UnmappedKeys { get; set; } = new();

        // ── Desktop only ─────────────────────────────────────────────────────────────────
        /// <summary>When the Chart of Accounts / Class List were last uploaded from QuickBooks Desktop.</summary>
        public DateTime? ListsUploadedAtUtc { get; set; }
        /// <summary>The PROD value from the uploaded file, e.g. "QuickBooks Pro".</summary>
        public string? CompanyProduct { get; set; }
        public int AccountCount { get; set; }
        public int ClassCount { get; set; }
        /// <summary>True once any file has been exported (live or voided); the start date is then fixed.</summary>
        public bool HasExported { get; set; }
    }
}
