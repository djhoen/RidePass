using Services.Helpers.Interfaces;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    /// <inheritdoc cref="IDistributorCredentialRepository"/>
    public class DistributorCredentialRepository : IDistributorCredentialRepository
    {
        private readonly IDbHelper _db;

        public DistributorCredentialRepository(IDbHelper db)
        {
            _db = db;
        }

        private const string Cols = @"
            id AS Id, tenant_id AS TenantId, distributor AS Distributor,
            account_number AS AccountNumber, username AS Username,
            password_encrypted AS PasswordEncrypted, api_key_encrypted AS ApiKeyEncrypted,
            is_enabled AS IsEnabled, last_sync_at AS LastSyncAt, last_status AS LastStatus,
            last_error AS LastError, last_products_seen AS LastProductsSeen,
            last_variants_updated AS LastVariantsUpdated,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        public async Task<TenantDistributorCredential?> Get(Guid tenantId, string distributor)
        {
            var sql = $@"SELECT {Cols} FROM tenant_distributor_credential
                         WHERE tenant_id = @tenantId AND lower(distributor) = lower(@distributor)";
            return (await _db.Query<TenantDistributorCredential>(sql, new { tenantId, distributor }))
                .FirstOrDefault();
        }

        // No ciphertext in this projection, deliberately. The screen needs to know a key EXISTS,
        // never what it is, so the booleans are computed in SQL and the blobs never leave the DB.
        public async Task<List<DistributorConnectionStatus>> ListStatuses(Guid tenantId)
        {
            const string sql = @"
                SELECT distributor AS Distributor, account_number AS AccountNumber, username AS Username,
                       is_enabled AS IsEnabled,
                       (api_key_encrypted IS NOT NULL) AS HasApiKey,
                       (password_encrypted IS NOT NULL) AS HasPassword,
                       last_sync_at AS LastSyncAt, last_status AS LastStatus, last_error AS LastError,
                       last_products_seen AS LastProductsSeen, last_variants_updated AS LastVariantsUpdated
                FROM tenant_distributor_credential
                WHERE tenant_id = @tenantId
                ORDER BY distributor";
            return (await _db.Query<DistributorConnectionStatus>(sql, new { tenantId })).ToList();
        }

        public Task Upsert(Guid tenantId, string distributor, string? accountNumber, string? username,
            string? passwordEncrypted, string? apiKeyEncrypted, bool isEnabled)
        {
            // COALESCE on the two secrets: a null means "unchanged", not "clear it". The UI never
            // shows a stored key, so making an unrelated edit re-key the credential would be a trap.
            // Deleting the connection is how you actually clear one.
            const string sql = @"
                INSERT INTO tenant_distributor_credential
                    (tenant_id, distributor, account_number, username, password_encrypted, api_key_encrypted, is_enabled)
                VALUES (@tenantId, lower(@distributor), @accountNumber, @username, @passwordEncrypted, @apiKeyEncrypted, @isEnabled)
                ON CONFLICT (tenant_id, distributor) DO UPDATE SET
                    account_number     = EXCLUDED.account_number,
                    username           = EXCLUDED.username,
                    password_encrypted = COALESCE(EXCLUDED.password_encrypted, tenant_distributor_credential.password_encrypted),
                    api_key_encrypted  = COALESCE(EXCLUDED.api_key_encrypted, tenant_distributor_credential.api_key_encrypted),
                    is_enabled         = EXCLUDED.is_enabled,
                    updated_at         = now()";
            return _db.Execute(sql, new
            {
                tenantId, distributor, accountNumber, username, passwordEncrypted, apiKeyEncrypted, isEnabled,
            });
        }

        public Task Delete(Guid tenantId, string distributor)
        {
            const string sql = @"DELETE FROM tenant_distributor_credential
                                 WHERE tenant_id = @tenantId AND lower(distributor) = lower(@distributor)";
            return _db.Execute(sql, new { tenantId, distributor });
        }

        // Tenant-spanning by design: the background sweep has no tenant context. Each row carries
        // its own TenantId and every write the sync performs is scoped by THAT, not by anything
        // ambient. 'running' rows are excluded so a sync that is mid-flight (or a process that died
        // holding one) isn't picked up again by the next tick.
        public async Task<List<TenantDistributorCredential>> ListDueForSync(DateTime staleBefore, int limit = 50)
        {
            var sql = $@"
                SELECT {Cols} FROM tenant_distributor_credential
                WHERE is_enabled
                  AND (last_status IS DISTINCT FROM 'running' OR last_sync_at < @stuckBefore)
                  AND (last_sync_at IS NULL OR last_sync_at < @staleBefore)
                ORDER BY last_sync_at NULLS FIRST
                LIMIT @limit";
            return (await _db.Query<TenantDistributorCredential>(sql, new
            {
                staleBefore,
                // A 'running' row whose run STARTED more than six hours ago is a crashed sync, not
                // a live one, so it stops being a reason to skip. Six hours absolute, deliberately
                // NOT relative to staleBefore: `staleBefore - 6h` would make the threshold 30 hours
                // and a crashed sync would sit wedged for an extra cycle. The staleness clause
                // below still governs the normal cadence, so recovery lands on the next due tick.
                stuckBefore = DateTime.UtcNow.AddHours(-6),
                limit,
            })).ToList();
        }

        public Task MarkRunning(Guid id) => _db.Execute(
            @"UPDATE tenant_distributor_credential
              SET last_status = 'running', last_sync_at = now(), updated_at = now()
              WHERE id = @id", new { id });

        public Task MarkResult(Guid id, string status, string? error, int productsSeen, int variantsUpdated) =>
            _db.Execute(
                @"UPDATE tenant_distributor_credential
                  SET last_status = @status, last_error = @error, last_sync_at = now(),
                      last_products_seen = @productsSeen, last_variants_updated = @variantsUpdated,
                      updated_at = now()
                  WHERE id = @id",
                new { id, status, error, productsSeen, variantsUpdated });
    }
}
