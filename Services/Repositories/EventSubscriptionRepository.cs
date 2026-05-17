using Services.Helpers.Interfaces;
using Services.Repositories.Data.EventData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EventSubscriptionRepository : IEventSubscriptionRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, user_id AS UserId,
            email, phone,
            notify_email AS NotifyEmail, notify_sms AS NotifySms,
            unsubscribe_token AS UnsubscribeToken,
            unsubscribed_at AS UnsubscribedAt,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public EventSubscriptionRepository(IDbHelper db) => _db = db;

        public async Task<EventSubscription?> GetByTenantAndEmail(Guid tenantId, string email)
        {
            var sql = $@"
                SELECT {Columns}
                FROM event_subscription
                WHERE tenant_id = @tenantId AND LOWER(email) = LOWER(@email)
                LIMIT 1";
            return (await _db.Query<EventSubscription>(sql, new { tenantId, email })).FirstOrDefault();
        }

        public async Task<EventSubscription?> GetByUnsubscribeToken(Guid token)
        {
            var sql = $"SELECT {Columns} FROM event_subscription WHERE unsubscribe_token = @token LIMIT 1";
            return (await _db.Query<EventSubscription>(sql, new { token })).FirstOrDefault();
        }

        public async Task<List<EventSubscription>> ListActiveForTenant(Guid tenantId)
        {
            var sql = $@"
                SELECT {Columns}
                FROM event_subscription
                WHERE tenant_id = @tenantId
                  AND unsubscribed_at IS NULL
                  AND (notify_email = true OR notify_sms = true)";
            return (await _db.Query<EventSubscription>(sql, new { tenantId })).ToList();
        }

        public async Task<Guid> Upsert(EventSubscription sub)
        {
            // If a row already exists for (tenant, email), update channels + clear unsubscribed_at.
            // Otherwise insert fresh.
            const string sql = @"
                INSERT INTO event_subscription
                    (tenant_id, user_id, email, phone, notify_email, notify_sms)
                VALUES
                    (@TenantId, @UserId, @Email, @Phone, @NotifyEmail, @NotifySms)
                ON CONFLICT (tenant_id, LOWER(email)) DO UPDATE SET
                    user_id = COALESCE(EXCLUDED.user_id, event_subscription.user_id),
                    phone = EXCLUDED.phone,
                    notify_email = EXCLUDED.notify_email,
                    notify_sms = EXCLUDED.notify_sms,
                    unsubscribed_at = NULL,
                    updated_at = now()
                RETURNING id";
            return (await _db.Query<Guid>(sql, sub)).First();
        }

        public async Task SetUnsubscribed(Guid id, bool unsubscribed)
        {
            const string sql = @"
                UPDATE event_subscription
                SET unsubscribed_at = CASE WHEN @unsubscribed THEN now() ELSE NULL END,
                    updated_at = now()
                WHERE id = @id";
            await _db.Execute(sql, new { id, unsubscribed });
        }
    }
}
