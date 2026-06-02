using Services.Helpers.Interfaces;
using Services.Repositories.Data.MessagingData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantSmsOptOutRepository : ITenantSmsOptOutRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, phone, opted_out AS OptedOut,
            opted_out_at_utc AS OptedOutAt, opted_in_at_utc AS OptedInAt,
            last_keyword AS LastKeyword,
            created_at_utc AS CreatedAt, updated_at_utc AS UpdatedAt";

        private readonly IDbHelper _db;

        public TenantSmsOptOutRepository(IDbHelper db) => _db = db;

        public async Task<bool> IsOptedOut(Guid tenantId, string phone)
        {
            // Partial index ix_tenant_sms_opt_out_active makes this an
            // index-only lookup on (tenant_id, phone) for the suppressed
            // subset — fastest path for the hot read.
            const string sql = @"
                SELECT 1
                FROM tenant_sms_opt_out
                WHERE tenant_id = @tenantId
                  AND phone = @phone
                  AND opted_out = true
                LIMIT 1";
            var hit = await _db.Query<int>(sql, new { tenantId, phone });
            return hit.Any();
        }

        public async Task RecordOptOut(Guid tenantId, string phone, string keyword)
        {
            // Upsert: first STOP from a phone inserts opted_out=true with
            // opted_out_at_utc = now; a subsequent STOP after a previous
            // START re-flips it and refreshes the timestamp. opted_in_at_utc
            // is left untouched so audit can still see when they last opted
            // in before this opt-out.
            const string sql = @"
                INSERT INTO tenant_sms_opt_out
                    (tenant_id, phone, opted_out, opted_out_at_utc, last_keyword)
                VALUES
                    (@tenantId, @phone, true, now(), @keyword)
                ON CONFLICT (tenant_id, phone) DO UPDATE
                    SET opted_out = true,
                        opted_out_at_utc = now(),
                        last_keyword = EXCLUDED.last_keyword,
                        updated_at_utc = now()";
            await _db.Execute(sql, new { tenantId, phone, keyword });
        }

        public async Task RecordOptIn(Guid tenantId, string phone, string keyword)
        {
            // Mirror of RecordOptOut. Inserting a fresh row with opted_out=false
            // is a no-op for the suppression check but still records that the
            // customer affirmatively consented — useful if a future flow wants
            // to require explicit opt-in before any send.
            const string sql = @"
                INSERT INTO tenant_sms_opt_out
                    (tenant_id, phone, opted_out, opted_in_at_utc, last_keyword)
                VALUES
                    (@tenantId, @phone, false, now(), @keyword)
                ON CONFLICT (tenant_id, phone) DO UPDATE
                    SET opted_out = false,
                        opted_in_at_utc = now(),
                        last_keyword = EXCLUDED.last_keyword,
                        updated_at_utc = now()";
            await _db.Execute(sql, new { tenantId, phone, keyword });
        }

        public async Task<List<TenantSmsOptOut>> ListForTenant(Guid tenantId, int take = 500)
        {
            var sql = $@"
                SELECT {Columns}
                FROM tenant_sms_opt_out
                WHERE tenant_id = @tenantId
                ORDER BY updated_at_utc DESC
                LIMIT @take";
            return (await _db.Query<TenantSmsOptOut>(sql, new { tenantId, take })).ToList();
        }
    }
}
