using Services.Helpers.Interfaces;
using Services.Repositories.Data.NotificationData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class NotificationPreferenceRepository : INotificationPreferenceRepository
    {
        private const string Columns = @"
            id, user_id AS UserId, kind, email_enabled AS EmailEnabled,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public NotificationPreferenceRepository(IDbHelper db) => _db = db;

        public async Task<List<NotificationPreference>> ListForUser(Guid userId)
        {
            var sql = $@"SELECT {Columns} FROM notification_preference WHERE user_id = @userId";
            return (await _db.Query<NotificationPreference>(sql, new { userId })).ToList();
        }

        public async Task<bool> IsEmailEnabled(Guid userId, string kind)
        {
            const string sql = @"
                SELECT email_enabled
                FROM notification_preference
                WHERE user_id = @userId AND kind = @kind
                LIMIT 1";
            var rows = await _db.Query<bool?>(sql, new { userId, kind });
            return rows.FirstOrDefault() ?? true;   // default = enabled
        }

        public async Task Upsert(Guid userId, string kind, bool emailEnabled)
        {
            const string sql = @"
                INSERT INTO notification_preference (user_id, kind, email_enabled)
                VALUES (@userId, @kind, @emailEnabled)
                ON CONFLICT (user_id, kind)
                DO UPDATE SET email_enabled = EXCLUDED.email_enabled";
            await _db.Execute(sql, new { userId, kind, emailEnabled });
        }
    }
}
