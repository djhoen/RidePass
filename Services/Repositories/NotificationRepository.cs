using Services.Helpers.Interfaces;
using Services.Repositories.Data.NotificationData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class NotificationRepository : INotificationRepository
    {
        private const string Columns = @"
            id, recipient_user_id AS RecipientUserId, tenant_id AS TenantId,
            kind, title, body, link_url AS LinkUrl, is_read AS IsRead,
            created_at AS CreatedAt, read_at AS ReadAt";

        private readonly IDbHelper _db;

        public NotificationRepository(IDbHelper db) => _db = db;

        public async Task<Guid> Insert(Notification n)
        {
            const string sql = @"
                INSERT INTO notification
                    (recipient_user_id, tenant_id, kind, title, body, link_url)
                VALUES
                    (@RecipientUserId, @TenantId, @Kind, @Title, @Body, @LinkUrl)
                RETURNING id";
            return (await _db.Query<Guid>(sql, n)).First();
        }

        public async Task<List<Notification>> ListForUser(Guid userId, int take = 50)
        {
            var sql = $@"
                SELECT {Columns}
                FROM notification
                WHERE recipient_user_id = @userId
                ORDER BY created_at DESC
                LIMIT @take";
            return (await _db.Query<Notification>(sql, new { userId, take })).ToList();
        }

        public async Task<int> CountUnread(Guid userId)
        {
            const string sql = @"
                SELECT COUNT(*)::int
                FROM notification
                WHERE recipient_user_id = @userId AND is_read = false";
            return (await _db.Query<int>(sql, new { userId })).FirstOrDefault();
        }

        public async Task MarkRead(Guid id, Guid userId)
        {
            const string sql = @"
                UPDATE notification
                SET is_read = true, read_at = now()
                WHERE id = @id AND recipient_user_id = @userId AND is_read = false";
            await _db.Execute(sql, new { id, userId });
        }

        public async Task MarkAllRead(Guid userId)
        {
            const string sql = @"
                UPDATE notification
                SET is_read = true, read_at = now()
                WHERE recipient_user_id = @userId AND is_read = false";
            await _db.Execute(sql, new { userId });
        }
    }
}
