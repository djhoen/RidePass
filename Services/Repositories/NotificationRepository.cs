using Dapper;
using Services.ExtensionMethods;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.NotificationData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly IDbHelper _dbHelper;
        public NotificationRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
        }

        public async Task<int> CreateNotification(Notification notification)
        {
            var sql = @"INSERT INTO ""notification"" (""recipientUserId"", ""notificationDate"", ""read"", ""subject"", ""body"", ""notificationTypeId"", ""fromUserId"", ""actionUrl"", ""actionText"")
                        VALUES (@recipientUserId, @notificationDate, @read, @subject, @body, @notificationTypeId, @fromUserId, @actionUrl, @actionText)
                        RETURNING ""id""";

            var result = await _dbHelper.Query<int>(sql, notification);

            return result.FirstOrDefault();
        }

        public async Task<List<int>> BulkCreateNotifications(List<string> recipientUserIds, Notification templateNotification)
        {
            if (recipientUserIds == null || !recipientUserIds.Any())
            {
                return new List<int>();
            }

            var date = templateNotification.NotificationDate == default(DateTime) ? DateTime.UtcNow : templateNotification.NotificationDate;
            var read = false;

            var values = string.Join(", ", recipientUserIds.Select((id, index) =>
                $"(@recipientUserId{index}, @notificationDate{index}, @read{index}, @subject{index}, @body{index}, @notificationTypeId{index}, @fromUserId{index}, @actionUrl{index}, @actionText{index})"));

            var sql = $@"INSERT INTO ""notification"" (""recipientUserId"", ""notificationDate"", ""read"", ""subject"", ""body"", ""notificationTypeId"", ""fromUserId"", ""actionUrl"", ""actionText"")
                        VALUES {values}
                        RETURNING ""id""";

            var parameters = new DynamicParameters();
            for (int i = 0; i < recipientUserIds.Count; i++)
            {
                parameters.Add($"recipientUserId{i}", recipientUserIds[i]);
                parameters.Add($"notificationDate{i}", date);
                parameters.Add($"read{i}", read);
                parameters.Add($"subject{i}", templateNotification.Subject);
                parameters.Add($"body{i}", templateNotification.Body);
                parameters.Add($"notificationTypeId{i}", templateNotification.NotificationTypeId);
                parameters.Add($"fromUserId{i}", templateNotification.FromUserId);
                parameters.Add($"actionUrl{i}", templateNotification.ActionUrl);
                parameters.Add($"actionText{i}", templateNotification.ActionText);
            }

            var result = await _dbHelper.Query<int>(sql, parameters);
            return result.ToList();
        }

        public async Task<List<Notification>> GetUserNotifications(string userId, int page)
        {
            var pageSize = 10;

            var sql = @"SELECT * FROM ""notification"" WHERE ""recipientUserId"" = @userId ORDER BY ""notificationDate"" DESC ";

            if (page <= 1)
            {
                sql += $"LIMIT {pageSize}";
            }
            else
            {
                var offset = (page - 1) * pageSize;
                sql += $"LIMIT {pageSize} OFFSET {offset}";
            }

            var result = await _dbHelper.Query<Notification>(sql, new { userId });

            var notifications = result.ToList();

            foreach (var notification in notifications)
            {
                notification.NotificationDateString = notification.NotificationDate.AsTimeAgo();
            }

            return notifications;
        }

        public async Task<int> GetUserUnreadNotificationCount(string userId)
        {
            var sql = @"SELECT count(*) FROM ""notification"" WHERE ""recipientUserId"" = @userId AND ""read"" = false";
            var result = await _dbHelper.Query<int>(sql, new { userId });
            return result.FirstOrDefault();
        }

        public async Task ReadUserNotifications(string userId)
        {
            var sql = @"UPDATE ""notification""
                        SET ""read"" = true
                        WHERE ""recipientUserId"" = @userId";

            await _dbHelper.Execute(sql, new { userId });
        }
    }
}
