using Services.Helpers.Interfaces;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EmailCampaignRepository : IEmailCampaignRepository
    {
        private const string CampaignColumns = @"
            id, tenant_id AS TenantId, subject, body_html AS BodyHtml, body_text AS BodyText,
            status, scheduled_for AS ScheduledFor, sent_at AS SentAt,
            recipient_count AS RecipientCount,
            created_by_user_id AS CreatedByUserId,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public EmailCampaignRepository(IDbHelper db) => _db = db;

        public async Task<List<EmailCampaign>> ListByTenant(Guid tenantId)
        {
            var sql = $@"
                SELECT {CampaignColumns}
                FROM email_campaign
                WHERE tenant_id = @tenantId
                ORDER BY created_at DESC";
            var r = await _db.Query<EmailCampaign>(sql, new { tenantId });
            return r.ToList();
        }

        public async Task<EmailCampaign?> GetById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {CampaignColumns}
                FROM email_campaign
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var r = await _db.Query<EmailCampaign>(sql, new { id, tenantId });
            return r.FirstOrDefault();
        }

        public async Task<Guid> Create(EmailCampaign c)
        {
            const string sql = @"
                INSERT INTO email_campaign
                    (tenant_id, subject, body_html, body_text, status,
                     scheduled_for, created_by_user_id)
                VALUES
                    (@TenantId, @Subject, @BodyHtml, @BodyText, @Status,
                     @ScheduledFor, @CreatedByUserId)
                RETURNING id";
            var r = await _db.Query<Guid>(sql, c);
            return r.First();
        }

        public async Task Update(EmailCampaign c)
        {
            const string sql = @"
                UPDATE email_campaign
                SET subject = @Subject,
                    body_html = @BodyHtml,
                    body_text = @BodyText,
                    status = @Status,
                    scheduled_for = @ScheduledFor
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, c);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM email_campaign WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task MarkSending(Guid id)
        {
            const string sql = "UPDATE email_campaign SET status = 'sending' WHERE id = @id";
            await _db.Execute(sql, new { id });
        }

        public async Task MarkScheduled(Guid id, DateTime scheduledForUtc)
        {
            const string sql = "UPDATE email_campaign SET status = 'scheduled', scheduled_for = @scheduledForUtc WHERE id = @id";
            await _db.Execute(sql, new { id, scheduledForUtc });
        }

        public async Task RevertToDraft(Guid id)
        {
            const string sql = "UPDATE email_campaign SET status = 'draft', scheduled_for = NULL WHERE id = @id";
            await _db.Execute(sql, new { id });
        }

        public async Task DeleteSendRows(Guid campaignId)
        {
            await _db.Execute("DELETE FROM email_campaign_send WHERE campaign_id = @campaignId", new { campaignId });
        }

        public async Task MarkSent(Guid id, int recipientCount)
        {
            const string sql = @"
                UPDATE email_campaign
                SET status = 'sent', sent_at = now(), recipient_count = @recipientCount
                WHERE id = @id";
            await _db.Execute(sql, new { id, recipientCount });
        }

        public async Task CreateSendRows(Guid campaignId, IEnumerable<EmailCampaignSend> sends)
        {
            const string sql = @"
                INSERT INTO email_campaign_send
                    (campaign_id, subscriber_id, email, name, status)
                VALUES (@CampaignId, @SubscriberId, @Email, @Name, @Status)
                ON CONFLICT (campaign_id, email) DO NOTHING";
            foreach (var s in sends)
            {
                s.CampaignId = campaignId;
                await _db.Execute(sql, s);
            }
        }

        public async Task<List<EmailCampaignSend>> ListSends(Guid campaignId)
        {
            const string sql = @"
                SELECT id, campaign_id AS CampaignId, subscriber_id AS SubscriberId,
                       email, name, sent_at AS SentAt, status, error
                FROM email_campaign_send
                WHERE campaign_id = @campaignId
                ORDER BY id";
            var r = await _db.Query<EmailCampaignSend>(sql, new { campaignId });
            return r.ToList();
        }

        public async Task UpdateSendStatus(Guid sendId, string status, string? error)
        {
            const string sql = @"
                UPDATE email_campaign_send
                SET status = @status,
                    error = @error,
                    sent_at = CASE WHEN @status = 'sent' THEN now() ELSE sent_at END
                WHERE id = @sendId";
            await _db.Execute(sql, new { sendId, status, error });
        }

        public async Task<int> CountSentEmailsInMonth(Guid tenantId, DateTime fromUtc, Guid excludeCampaignId)
        {
            // Tenant-scoped via the campaign join; counts delivered ('sent') rows since fromUtc,
            // excluding the campaign being billed so its own rows don't inflate the baseline.
            const string sql = @"
                SELECT COUNT(*)::int
                FROM email_campaign_send s
                JOIN email_campaign c ON c.id = s.campaign_id
                WHERE c.tenant_id = @tenantId
                  AND s.status = 'sent'
                  AND s.sent_at >= @fromUtc
                  AND s.campaign_id <> @excludeCampaignId";
            var r = await _db.Query<int>(sql, new { tenantId, fromUtc, excludeCampaignId });
            return r.FirstOrDefault();
        }
    }
}
