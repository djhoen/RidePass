namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>Everything the settings screen needs to render the QuickBooks panel in one call.</summary>
    public class QuickBooksStatusResponse
    {
        /// <summary>False when the Intuit app credentials aren't configured on this deployment at all.</summary>
        public bool IsConfigured { get; set; }
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
    }
}
