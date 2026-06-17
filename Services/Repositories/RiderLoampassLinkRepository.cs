using Services.Helpers.Interfaces;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RiderLoampassLinkRepository : IRiderLoampassLinkRepository
    {
        private const string SelectColumns = @"
            id, user_id AS UserId, tenant_id AS TenantId,
            loampass_account_id AS LoampassAccountId, loampass_email AS LoampassEmail,
            linked_at_utc AS LinkedAtUtc";

        private readonly IDbHelper _db;

        public RiderLoampassLinkRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<List<RiderLoampassLink>> ListByUserId(Guid userId, Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectColumns}
                FROM rider_loampass_link
                WHERE user_id = @userId AND tenant_id = @tenantId
                ORDER BY linked_at_utc";
            var result = await _db.Query<RiderLoampassLink>(sql, new { userId, tenantId });
            return result.ToList();
        }

        public async Task Add(RiderLoampassLink link)
        {
            // Re-linking the same LoamMx account is a no-op refresh (one row per user+account).
            const string sql = @"
                INSERT INTO rider_loampass_link (user_id, tenant_id, loampass_account_id, loampass_email)
                VALUES (@UserId, @TenantId, @LoampassAccountId, @LoampassEmail)
                ON CONFLICT (user_id, loampass_account_id) DO UPDATE SET
                    loampass_email = EXCLUDED.loampass_email,
                    linked_at_utc  = now()";
            await _db.Execute(sql, link);
        }

        public async Task<Guid?> GetUserIdByAccount(string loampassAccountId, Guid tenantId)
        {
            const string sql = @"
                SELECT user_id FROM rider_loampass_link
                WHERE loampass_account_id = @loampassAccountId AND tenant_id = @tenantId
                LIMIT 1";
            var rows = (await _db.Query<Guid>(sql, new { loampassAccountId, tenantId })).ToList();
            return rows.Count > 0 ? rows[0] : (Guid?)null;
        }

        public async Task DeleteByAccount(Guid userId, Guid tenantId, string loampassAccountId)
        {
            await _db.Execute(
                @"DELETE FROM rider_loampass_link
                  WHERE user_id = @userId AND tenant_id = @tenantId AND loampass_account_id = @loampassAccountId",
                new { userId, tenantId, loampassAccountId });
        }
    }
}
