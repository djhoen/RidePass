using Services.Helpers.Interfaces;
using Services.Repositories.Data.QuickBooksData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class QuickBooksRepository : IQuickBooksRepository
    {
        private const string ConnectionColumns = @"
            id, tenant_id AS TenantId, realm_id AS RealmId,
            refresh_token_encrypted AS RefreshTokenEncrypted,
            refresh_token_expires_at_utc AS RefreshTokenExpiresAtUtc,
            access_token_encrypted AS AccessTokenEncrypted,
            access_token_expires_at_utc AS AccessTokenExpiresAtUtc,
            status, sync_enabled AS SyncEnabled,
            sync_start_date AS SyncStartDate, last_synced_date AS LastSyncedDate,
            last_sync_at_utc AS LastSyncAtUtc, last_sync_error AS LastSyncError,
            connected_by_user_id AS ConnectedByUserId, connected_at_utc AS ConnectedAtUtc,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string MappingColumns = @"
            id, tenant_id AS TenantId, mapping_key AS MappingKey,
            qbo_account_id AS QboAccountId, qbo_account_name AS QboAccountName,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string ClassMappingColumns = @"
            id, tenant_id AS TenantId, bucket_key AS BucketKey,
            qbo_class_id AS QboClassId, qbo_class_name AS QboClassName,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string SyncLogColumns = @"
            id, tenant_id AS TenantId, business_date AS BusinessDate, status,
            qbo_journal_entry_id AS QboJournalEntryId, qbo_doc_number AS QboDocNumber,
            entry_count AS EntryCount, total_debits_cents AS TotalDebitsCents,
            attempt_count AS AttemptCount, last_error AS LastError,
            synced_at_utc AS SyncedAtUtc, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public QuickBooksRepository(IDbHelper db) => _db = db;

        // ── Connection ───────────────────────────────────────────────────────────────────

        public async Task<QuickBooksConnection?> GetConnection(Guid tenantId)
        {
            var sql = $"SELECT {ConnectionColumns} FROM tenant_quickbooks_connection WHERE tenant_id = @tenantId";
            return (await _db.Query<QuickBooksConnection>(sql, new { tenantId })).FirstOrDefault();
        }

        public async Task<List<QuickBooksConnection>> ListSyncableConnections()
        {
            // Deliberately not tenant-scoped: this drives the cross-tenant nightly sweep, the same
            // shape as MonthlyPayoutDrafter. Callers must scope everything downstream by the
            // TenantId on each row they get back.
            var sql = $@"
                SELECT {ConnectionColumns}
                FROM tenant_quickbooks_connection
                WHERE status = 'active' AND sync_enabled = true
                ORDER BY tenant_id";
            return (await _db.Query<QuickBooksConnection>(sql)).ToList();
        }

        public async Task<Guid> UpsertConnection(QuickBooksConnection c)
        {
            // Re-auth of an existing link lands here too, so the update deliberately resets status
            // and clears the last error, a tenant reconnecting has fixed whatever was wrong.
            // sync_start_date and the cursor are NOT reset: re-authing must not re-post history.
            const string sql = @"
                INSERT INTO tenant_quickbooks_connection
                    (tenant_id, realm_id, refresh_token_encrypted, refresh_token_expires_at_utc,
                     access_token_encrypted, access_token_expires_at_utc, status, sync_enabled,
                     sync_start_date, connected_by_user_id, connected_at_utc)
                VALUES
                    (@TenantId, @RealmId, @RefreshTokenEncrypted, @RefreshTokenExpiresAtUtc,
                     @AccessTokenEncrypted, @AccessTokenExpiresAtUtc, @Status, @SyncEnabled,
                     @SyncStartDate, @ConnectedByUserId, now())
                ON CONFLICT (tenant_id) DO UPDATE SET
                    realm_id                     = EXCLUDED.realm_id,
                    refresh_token_encrypted      = EXCLUDED.refresh_token_encrypted,
                    refresh_token_expires_at_utc = EXCLUDED.refresh_token_expires_at_utc,
                    access_token_encrypted       = EXCLUDED.access_token_encrypted,
                    access_token_expires_at_utc  = EXCLUDED.access_token_expires_at_utc,
                    status                       = EXCLUDED.status,
                    last_sync_error              = NULL,
                    connected_by_user_id         = EXCLUDED.connected_by_user_id,
                    connected_at_utc             = now()
                RETURNING id";
            return (await _db.Query<Guid>(sql, c)).First();
        }

        public async Task UpdateTokens(Guid tenantId, string refreshTokenEncrypted, DateTime? refreshExpiresAtUtc,
                                       string accessTokenEncrypted, DateTime accessExpiresAtUtc)
        {
            const string sql = @"
                UPDATE tenant_quickbooks_connection
                   SET refresh_token_encrypted      = @refreshTokenEncrypted,
                       refresh_token_expires_at_utc = @refreshExpiresAtUtc,
                       access_token_encrypted       = @accessTokenEncrypted,
                       access_token_expires_at_utc  = @accessExpiresAtUtc,
                       status                       = 'active',
                       last_sync_error              = NULL
                 WHERE tenant_id = @tenantId";
            await _db.Execute(sql, new { tenantId, refreshTokenEncrypted, refreshExpiresAtUtc, accessTokenEncrypted, accessExpiresAtUtc });
        }

        public async Task SetStatus(Guid tenantId, string status, string? error)
        {
            const string sql = @"
                UPDATE tenant_quickbooks_connection
                   SET status = @status, last_sync_error = @error
                 WHERE tenant_id = @tenantId";
            await _db.Execute(sql, new { tenantId, status, error });
        }

        public async Task SetSyncEnabled(Guid tenantId, bool enabled)
        {
            const string sql = "UPDATE tenant_quickbooks_connection SET sync_enabled = @enabled WHERE tenant_id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled });
        }

        public async Task SetSyncCursor(Guid tenantId, DateOnly lastSyncedDate, DateTime atUtc)
        {
            // GREATEST so an out-of-order backfill of an older day can never rewind the cursor.
            const string sql = @"
                UPDATE tenant_quickbooks_connection
                   SET last_synced_date = GREATEST(COALESCE(last_synced_date, @lastSyncedDate), @lastSyncedDate),
                       last_sync_at_utc = @atUtc,
                       last_sync_error  = NULL
                 WHERE tenant_id = @tenantId";
            await _db.Execute(sql, new { tenantId, lastSyncedDate, atUtc });
        }

        public async Task DeleteConnection(Guid tenantId)
        {
            // Mappings and the sync log survive a disconnect on purpose: a tenant who reconnects
            // gets their chart-of-accounts mapping back, and the log stays auditable for the days
            // that were already posted into their books.
            await _db.Execute("DELETE FROM tenant_quickbooks_connection WHERE tenant_id = @tenantId", new { tenantId });
        }

        // ── Account mapping ──────────────────────────────────────────────────────────────

        public async Task<List<QboAccountMapping>> ListMappings(Guid tenantId)
        {
            var sql = $"SELECT {MappingColumns} FROM qbo_account_mapping WHERE tenant_id = @tenantId ORDER BY mapping_key";
            return (await _db.Query<QboAccountMapping>(sql, new { tenantId })).ToList();
        }

        public async Task UpsertMapping(Guid tenantId, string mappingKey, string qboAccountId, string? qboAccountName)
        {
            const string sql = @"
                INSERT INTO qbo_account_mapping (tenant_id, mapping_key, qbo_account_id, qbo_account_name)
                VALUES (@tenantId, @mappingKey, @qboAccountId, @qboAccountName)
                ON CONFLICT (tenant_id, mapping_key) DO UPDATE SET
                    qbo_account_id   = EXCLUDED.qbo_account_id,
                    qbo_account_name = EXCLUDED.qbo_account_name";
            await _db.Execute(sql, new { tenantId, mappingKey, qboAccountId, qboAccountName });
        }

        public async Task DeleteMapping(Guid tenantId, string mappingKey)
        {
            await _db.Execute("DELETE FROM qbo_account_mapping WHERE tenant_id = @tenantId AND mapping_key = @mappingKey",
                new { tenantId, mappingKey });
        }

        // ── Class mapping ────────────────────────────────────────────────────────────────

        public async Task<List<QboClassMapping>> ListClassMappings(Guid tenantId)
        {
            var sql = $"SELECT {ClassMappingColumns} FROM qbo_class_mapping WHERE tenant_id = @tenantId ORDER BY bucket_key";
            return (await _db.Query<QboClassMapping>(sql, new { tenantId })).ToList();
        }

        public async Task UpsertClassMapping(Guid tenantId, string bucketKey, string qboClassId, string? qboClassName)
        {
            const string sql = @"
                INSERT INTO qbo_class_mapping (tenant_id, bucket_key, qbo_class_id, qbo_class_name)
                VALUES (@tenantId, @bucketKey, @qboClassId, @qboClassName)
                ON CONFLICT (tenant_id, bucket_key) DO UPDATE SET
                    qbo_class_id   = EXCLUDED.qbo_class_id,
                    qbo_class_name = EXCLUDED.qbo_class_name";
            await _db.Execute(sql, new { tenantId, bucketKey, qboClassId, qboClassName });
        }

        public async Task DeleteClassMapping(Guid tenantId, string bucketKey)
        {
            await _db.Execute("DELETE FROM qbo_class_mapping WHERE tenant_id = @tenantId AND bucket_key = @bucketKey",
                new { tenantId, bucketKey });
        }

        // ── Sync log ─────────────────────────────────────────────────────────────────────

        public async Task<QboSyncLogEntry?> GetSyncLog(Guid tenantId, DateOnly businessDate)
        {
            var sql = $"SELECT {SyncLogColumns} FROM qbo_sync_log WHERE tenant_id = @tenantId AND business_date = @businessDate";
            return (await _db.Query<QboSyncLogEntry>(sql, new { tenantId, businessDate })).FirstOrDefault();
        }

        public async Task<List<QboSyncLogEntry>> ListSyncLog(Guid tenantId, int take = 60)
        {
            var sql = $@"
                SELECT {SyncLogColumns} FROM qbo_sync_log
                WHERE tenant_id = @tenantId
                ORDER BY business_date DESC
                LIMIT @take";
            return (await _db.Query<QboSyncLogEntry>(sql, new { tenantId, take })).ToList();
        }

        public async Task<bool> TryClaimBusinessDate(Guid tenantId, DateOnly businessDate)
        {
            // The atomic guard against double-posting revenue into a customer's live books.
            //
            // Inserts the day as 'failed' (= claimed, not yet proven) and returns true. If the row
            // already exists, the DO UPDATE only fires when the previous attempt did NOT succeed, // so an already-posted day matches nothing, RETURNING yields no row, and we report "not
            // claimed". A crash mid-post leaves the row 'failed', which is honest and retryable.
            //
            // Two dispatchers racing the same day: Postgres serialises them on the unique index, so
            // exactly one insert wins and the loser takes the DO UPDATE branch against a row that is
            // either 'failed' (it retries, safe, nothing was posted) or 'success' (it backs off).
            const string sql = @"
                INSERT INTO qbo_sync_log (tenant_id, business_date, status, attempt_count)
                VALUES (@tenantId, @businessDate, 'failed', 1)
                ON CONFLICT (tenant_id, business_date) DO UPDATE
                    SET attempt_count = qbo_sync_log.attempt_count + 1
                    WHERE qbo_sync_log.status <> 'success'
                RETURNING id";
            var claimed = await _db.Query<Guid>(sql, new { tenantId, businessDate });
            return claimed.Any();
        }

        public async Task RecordSyncOutcome(QboSyncLogEntry e)
        {
            const string sql = @"
                INSERT INTO qbo_sync_log
                    (tenant_id, business_date, status, qbo_journal_entry_id, qbo_doc_number,
                     entry_count, total_debits_cents, attempt_count, last_error, synced_at_utc)
                VALUES
                    (@TenantId, @BusinessDate, @Status, @QboJournalEntryId, @QboDocNumber,
                     @EntryCount, @TotalDebitsCents, 1, @LastError, @SyncedAtUtc)
                ON CONFLICT (tenant_id, business_date) DO UPDATE SET
                    status               = EXCLUDED.status,
                    qbo_journal_entry_id = COALESCE(EXCLUDED.qbo_journal_entry_id, qbo_sync_log.qbo_journal_entry_id),
                    qbo_doc_number       = COALESCE(EXCLUDED.qbo_doc_number, qbo_sync_log.qbo_doc_number),
                    entry_count          = EXCLUDED.entry_count,
                    total_debits_cents   = EXCLUDED.total_debits_cents,
                    last_error           = EXCLUDED.last_error,
                    synced_at_utc        = EXCLUDED.synced_at_utc";
            await _db.Execute(sql, e);
        }
    }
}
