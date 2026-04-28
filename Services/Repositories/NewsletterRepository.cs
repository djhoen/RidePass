using Services.Helpers.Interfaces;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class NewsletterRepository : INewsletterRepository
    {
        private const string SubscriberColumns = @"
            id, tenant_id AS TenantId, email, name, source,
            unsubscribe_token AS UnsubscribeToken,
            subscribed_at AS SubscribedAt,
            unsubscribed_at AS UnsubscribedAt";

        private readonly IDbHelper _db;

        public NewsletterRepository(IDbHelper db) => _db = db;

        public async Task<List<NewsletterSubscriber>> ListByTenant(Guid tenantId, bool includeUnsubscribed)
        {
            var filter = includeUnsubscribed ? "" : "AND unsubscribed_at IS NULL";
            var sql = $@"
                SELECT {SubscriberColumns}
                FROM newsletter_subscriber
                WHERE tenant_id = @tenantId {filter}
                ORDER BY subscribed_at DESC";
            var r = await _db.Query<NewsletterSubscriber>(sql, new { tenantId });
            return r.ToList();
        }

        public async Task<NewsletterSubscriber?> GetByEmail(Guid tenantId, string email)
        {
            var sql = $@"
                SELECT {SubscriberColumns}
                FROM newsletter_subscriber
                WHERE tenant_id = @tenantId AND LOWER(email) = LOWER(@email)
                LIMIT 1";
            var r = await _db.Query<NewsletterSubscriber>(sql, new { tenantId, email });
            return r.FirstOrDefault();
        }

        public async Task<NewsletterSubscriber?> GetByUnsubscribeToken(Guid token)
        {
            var sql = $@"
                SELECT {SubscriberColumns}
                FROM newsletter_subscriber
                WHERE unsubscribe_token = @token
                LIMIT 1";
            var r = await _db.Query<NewsletterSubscriber>(sql, new { token });
            return r.FirstOrDefault();
        }

        public async Task<NewsletterSubscriber> UpsertFromSignup(Guid tenantId, string email, string? name, string source)
        {
            // If the address was previously unsubscribed, clear unsubscribed_at so this counts as
            // a re-subscribe. We do NOT overwrite a non-null name with null.
            var sql = $@"
                INSERT INTO newsletter_subscriber (tenant_id, email, name, source)
                VALUES (@tenantId, @email, @name, @source)
                ON CONFLICT (tenant_id, email) DO UPDATE
                    SET name = COALESCE(EXCLUDED.name, newsletter_subscriber.name),
                        unsubscribed_at = NULL
                RETURNING {SubscriberColumns}";
            var r = await _db.Query<NewsletterSubscriber>(sql, new { tenantId, email, name, source });
            return r.First();
        }

        public async Task Unsubscribe(Guid id)
        {
            const string sql = "UPDATE newsletter_subscriber SET unsubscribed_at = now() WHERE id = @id";
            await _db.Execute(sql, new { id });
        }

        public async Task Resubscribe(Guid id)
        {
            const string sql = "UPDATE newsletter_subscriber SET unsubscribed_at = NULL WHERE id = @id";
            await _db.Execute(sql, new { id });
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM newsletter_subscriber WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task<int> CountActive(Guid tenantId)
        {
            const string sql = @"
                SELECT COUNT(*)::int FROM newsletter_subscriber
                WHERE tenant_id = @tenantId AND unsubscribed_at IS NULL";
            var r = await _db.Query<int>(sql, new { tenantId });
            return r.FirstOrDefault();
        }

        public async Task<List<NewsletterSubscriber>> ListActiveForSend(Guid tenantId)
        {
            var sql = $@"
                SELECT {SubscriberColumns}
                FROM newsletter_subscriber
                WHERE tenant_id = @tenantId AND unsubscribed_at IS NULL
                ORDER BY subscribed_at";
            var r = await _db.Query<NewsletterSubscriber>(sql, new { tenantId });
            return r.ToList();
        }
    }
}
