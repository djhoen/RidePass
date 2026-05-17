using Services.Helpers.Interfaces;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class PasswordResetRepository : IPasswordResetRepository
    {
        private readonly IDbHelper _db;
        public PasswordResetRepository(IDbHelper db) => _db = db;

        public async Task<Guid> Insert(PasswordResetToken token)
        {
            const string sql = @"
                INSERT INTO password_reset_token (user_id, token_hash, expires_at_utc)
                VALUES (@UserId, @TokenHash, @ExpiresAtUtc)
                RETURNING id";
            return (await _db.Query<Guid>(sql, token)).First();
        }

        public async Task<PasswordResetToken?> GetByTokenHash(string tokenHash)
        {
            const string sql = @"
                SELECT id, user_id AS UserId, token_hash AS TokenHash,
                       expires_at_utc AS ExpiresAtUtc, used_at_utc AS UsedAtUtc, created_at AS CreatedAt
                FROM password_reset_token
                WHERE token_hash = @tokenHash
                LIMIT 1";
            return (await _db.Query<PasswordResetToken>(sql, new { tokenHash })).FirstOrDefault();
        }

        public async Task MarkUsed(Guid id)
        {
            const string sql = "UPDATE password_reset_token SET used_at_utc = now() WHERE id = @id";
            await _db.Execute(sql, new { id });
        }
    }
}
