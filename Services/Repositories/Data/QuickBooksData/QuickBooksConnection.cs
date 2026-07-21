namespace Services.Repositories.Data.QuickBooksData
{
    /// <summary>
    /// A tenant's OAuth link to one QuickBooks Online company (tenant_quickbooks_connection).
    /// Token fields hold EncryptionHelper ciphertext, never the raw token, read them through
    /// IQuickBooksTokenService, which owns decrypt + refresh.
    /// </summary>
    public class QuickBooksConnection
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>The QBO company id. Every API call is scoped to it.</summary>
        public string RealmId { get; set; } = null!;
        public string RefreshTokenEncrypted { get; set; } = null!;
        /// <summary>Intuit expires refresh tokens (~100 days) and rotates them on nearly every refresh.</summary>
        public DateTime? RefreshTokenExpiresAtUtc { get; set; }
        public string? AccessTokenEncrypted { get; set; }
        public DateTime? AccessTokenExpiresAtUtc { get; set; }
        /// <summary>active | expired | revoked | error</summary>
        public string Status { get; set; } = "active";
        public bool SyncEnabled { get; set; } = true;
        /// <summary>Nothing before this business date is ever posted. Set at connect time.</summary>
        public DateOnly SyncStartDate { get; set; }
        /// <summary>Cursor: most recent business date successfully posted. Null = nothing posted yet.</summary>
        public DateOnly? LastSyncedDate { get; set; }
        public DateTime? LastSyncAtUtc { get; set; }
        public string? LastSyncError { get; set; }
        public Guid? ConnectedByUserId { get; set; }
        public DateTime ConnectedAtUtc { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
