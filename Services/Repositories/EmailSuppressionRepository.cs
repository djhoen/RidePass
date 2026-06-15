using Services.Helpers.Interfaces;
using Services.Repositories.Data.EmailData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EmailSuppressionRepository : IEmailSuppressionRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, email, reason, scope,
            source, detail, created_at AS CreatedAt";

        private readonly IDbHelper _db;

        public EmailSuppressionRepository(IDbHelper db) => _db = db;

        public async Task Suppress(Guid? tenantId, string email, string reason, string scope, string? source, string? detail)
        {
            // Idempotent: the partial unique index dedupes by (tenant-or-global, lower(email), scope).
            // The ON CONFLICT target must restate the index expression exactly.
            const string sql = @"
                INSERT INTO email_suppression (tenant_id, email, reason, scope, source, detail)
                VALUES (@tenantId, @email, @reason, @scope, @source, @detail)
                ON CONFLICT (COALESCE(tenant_id, '00000000-0000-0000-0000-000000000000'::uuid), lower(email), scope)
                DO NOTHING";
            await _db.Execute(sql, new { tenantId, email, reason, scope, source, detail });
        }

        public async Task<bool> IsSuppressed(string email, Guid? tenantId, bool marketing)
        {
            // 'all'-scope rows block everything; 'marketing'-scope rows block only when this is a
            // marketing send. A NULL tenant_id row is platform-wide and applies to every tenant.
            const string sql = @"
                SELECT EXISTS (
                    SELECT 1 FROM email_suppression
                    WHERE lower(email) = lower(@email)
                      AND (tenant_id IS NULL OR tenant_id = @tenantId)
                      AND (scope = 'all' OR (@marketing AND scope = 'marketing'))
                )";
            var r = await _db.Query<bool>(sql, new { email, tenantId, marketing });
            return r.FirstOrDefault();
        }

        public async Task<HashSet<string>> ListMarketingBlocklist(Guid tenantId)
        {
            // Everything that blocks a marketing send for this tenant: all hard bounces ('all',
            // global or tenant) plus all marketing opt-outs ('marketing', global or tenant).
            const string sql = @"
                SELECT DISTINCT lower(email) AS email
                FROM email_suppression
                WHERE (tenant_id IS NULL OR tenant_id = @tenantId)
                  AND scope IN ('all', 'marketing')";
            var rows = await _db.Query<string>(sql, new { tenantId });
            return new HashSet<string>(rows, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<List<EmailSuppression>> ListForTenant(Guid tenantId, int take = 500)
        {
            // Tenant-scoped only. Platform-wide rows (tenant_id IS NULL) are deliberately
            // excluded: a global hard bounce can carry an address that originated from another
            // tenant's send, so exposing it here would leak addresses across tenants. Global
            // rows are still enforced at send time by IsSuppressed.
            var sql = $@"
                SELECT {Columns}
                FROM email_suppression
                WHERE tenant_id = @tenantId
                ORDER BY created_at DESC
                LIMIT @take";
            var r = await _db.Query<EmailSuppression>(sql, new { tenantId, take });
            return r.ToList();
        }

        public async Task RemoveForTenant(Guid id, Guid tenantId)
        {
            // Scoped delete: a tenant can only clear its own suppressions, never platform-wide ones.
            const string sql = "DELETE FROM email_suppression WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }
    }
}
