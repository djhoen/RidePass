using Services.Repositories.Data.QuickBooksData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// All per-tenant QuickBooks integration state: the OAuth connection, the chart-of-accounts
    /// mapping, and the post log. One repository because the three tables are only ever read and
    /// written together, by the sync and its settings screen.
    ///
    /// Every method takes tenantId and puts it in the WHERE, no method derives the tenant itself.
    /// </summary>
    public interface IQuickBooksRepository
    {
        // ── Connection ───────────────────────────────────────────────────────────────────
        Task<QuickBooksConnection?> GetConnection(Guid tenantId);
        /// <summary>Connections the nightly sweep should consider. Not tenant-scoped by design, the sweep spans tenants.</summary>
        Task<List<QuickBooksConnection>> ListSyncableConnections();
        /// <summary>Connect or re-connect. Keyed on tenant_id, so re-auth updates in place.</summary>
        Task<Guid> UpsertConnection(QuickBooksConnection connection);
        /// <summary>Persist a rotated token pair after a refresh. Intuit replaces the refresh token on most refreshes.</summary>
        Task UpdateTokens(Guid tenantId, string refreshTokenEncrypted, DateTime? refreshExpiresAtUtc,
                          string accessTokenEncrypted, DateTime accessExpiresAtUtc);
        Task SetStatus(Guid tenantId, string status, string? error);
        Task SetSyncEnabled(Guid tenantId, bool enabled);
        Task SetSyncCursor(Guid tenantId, DateOnly lastSyncedDate, DateTime atUtc);
        Task DeleteConnection(Guid tenantId);

        // ── Account mapping ──────────────────────────────────────────────────────────────
        Task<List<QboAccountMapping>> ListMappings(Guid tenantId);
        Task UpsertMapping(Guid tenantId, string mappingKey, string qboAccountId, string? qboAccountName);
        Task DeleteMapping(Guid tenantId, string mappingKey);

        // ── Class mapping ────────────────────────────────────────────────────────────────
        // The profit-center half of the mapping: which QBO Class each reporting bucket posts under.
        // Separate from the account mapping because it is optional, a tenant can run the sync
        // forever without a single class row, and because the key space is bucket keys, not
        // QboAccountKeys slots.
        Task<List<QboClassMapping>> ListClassMappings(Guid tenantId);
        Task UpsertClassMapping(Guid tenantId, string bucketKey, string qboClassId, string? qboClassName);
        Task DeleteClassMapping(Guid tenantId, string bucketKey);

        // ── Sync log ─────────────────────────────────────────────────────────────────────
        Task<QboSyncLogEntry?> GetSyncLog(Guid tenantId, DateOnly businessDate);
        Task<List<QboSyncLogEntry>> ListSyncLog(Guid tenantId, int take = 60);
        /// <summary>
        /// Record the outcome of posting one business date. Upserts on (tenant_id, business_date), /// the unique index that makes the whole sync idempotent.
        /// </summary>
        Task RecordSyncOutcome(QboSyncLogEntry entry);
        /// <summary>
        /// Claim a (tenant, business_date) for posting. Returns false if it's already been posted
        /// successfully, which is the last line of defense against double-posting into live books.
        /// </summary>
        Task<bool> TryClaimBusinessDate(Guid tenantId, DateOnly businessDate);
    }
}
