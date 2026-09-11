using Services.Helpers.Interfaces;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EmailEngagementRepository : IEmailEngagementRepository
    {
        private readonly IDbHelper _db;

        public EmailEngagementRepository(IDbHelper db) => _db = db;

        public async Task Record(EmailEngagement e)
        {
            // ON CONFLICT on the SendGrid event id: their webhook retries a delivery until we
            // answer 2xx, so the same event can arrive twice.
            const string sql = @"
                INSERT INTO email_engagement (tenant_id, source_kind, source_send_id, event, url, sg_event_id, occurred_at)
                VALUES (@TenantId, @SourceKind, @SourceSendId, @Event, @Url, @SgEventId, @OccurredAt)
                ON CONFLICT (sg_event_id) WHERE sg_event_id IS NOT NULL DO NOTHING";
            await _db.Execute(sql, e);
        }

        public async Task<Dictionary<Guid, EmailEngagementStats>> GetCampaignStats(Guid tenantId)
        {
            // Distinct people, not events: one rider opening five times is one open. Joined
            // through the send row so the campaign's tenant is enforced, not trusted.
            const string sql = @"
                SELECT cs.campaign_id AS Key,
                       COUNT(DISTINCT e.source_send_id) FILTER (WHERE e.event = 'open')::int  AS UniqueOpens,
                       COUNT(DISTINCT e.source_send_id) FILTER (WHERE e.event = 'click')::int AS UniqueClicks,
                       COUNT(*) FILTER (WHERE e.event = 'click')::int                          AS TotalClicks
                FROM email_engagement e
                JOIN email_campaign_send cs ON cs.id = e.source_send_id
                JOIN email_campaign c ON c.id = cs.campaign_id AND c.tenant_id = @tenantId
                WHERE e.tenant_id = @tenantId AND e.source_kind = 'campaign'
                GROUP BY cs.campaign_id";
            var rows = await _db.Query<EmailEngagementStats>(sql, new { tenantId });
            return rows.ToDictionary(r => r.Key);
        }

        public async Task<List<EmailClickUrlStats>> GetCampaignClickUrls(Guid campaignId, Guid tenantId)
        {
            const string sql = @"
                SELECT e.url AS Url,
                       COUNT(DISTINCT e.source_send_id)::int AS UniqueClickers,
                       COUNT(*)::int                          AS TotalClicks
                FROM email_engagement e
                JOIN email_campaign_send cs ON cs.id = e.source_send_id
                JOIN email_campaign c ON c.id = cs.campaign_id AND c.tenant_id = @tenantId
                WHERE e.tenant_id = @tenantId AND e.source_kind = 'campaign' AND e.event = 'click'
                  AND cs.campaign_id = @campaignId AND e.url IS NOT NULL
                GROUP BY e.url
                ORDER BY COUNT(DISTINCT e.source_send_id) DESC, COUNT(*) DESC
                LIMIT 50";
            return (await _db.Query<EmailClickUrlStats>(sql, new { campaignId, tenantId })).ToList();
        }

        public async Task<Dictionary<Guid, EmailEngagementStats>> GetAutomationStepStats(Guid automationId, Guid tenantId)
        {
            const string sql = @"
                SELECT ms.step_id AS Key,
                       COUNT(DISTINCT e.source_send_id) FILTER (WHERE e.event = 'open')::int  AS UniqueOpens,
                       COUNT(DISTINCT e.source_send_id) FILTER (WHERE e.event = 'click')::int AS UniqueClicks,
                       COUNT(*) FILTER (WHERE e.event = 'click')::int                          AS TotalClicks
                FROM email_engagement e
                JOIN marketing_automation_send ms ON ms.id = e.source_send_id AND ms.tenant_id = @tenantId
                WHERE e.tenant_id = @tenantId AND e.source_kind = 'automation' AND ms.automation_id = @automationId
                GROUP BY ms.step_id";
            var rows = await _db.Query<EmailEngagementStats>(sql, new { automationId, tenantId });
            return rows.ToDictionary(r => r.Key);
        }
    }

    public class EmailTemplateRepository : IEmailTemplateRepository
    {
        private readonly IDbHelper _db;

        public EmailTemplateRepository(IDbHelper db) => _db = db;

        private const string Columns = @"
            id AS Id, tenant_id AS TenantId, name AS Name, subject AS Subject, preview_text AS PreviewText,
            body_html AS BodyHtml, created_by_user_id AS CreatedByUserId, created_at AS CreatedAt, updated_at AS UpdatedAt";

        public async Task<List<EmailTemplate>> ListForTenant(Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM email_template WHERE tenant_id = @tenantId ORDER BY lower(name)";
            return (await _db.Query<EmailTemplate>(sql, new { tenantId })).ToList();
        }

        public async Task<EmailTemplate?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM email_template WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<EmailTemplate>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> Create(EmailTemplate t)
        {
            const string sql = @"
                INSERT INTO email_template (tenant_id, name, subject, preview_text, body_html, created_by_user_id)
                VALUES (@TenantId, @Name, @Subject, @PreviewText, @BodyHtml, @CreatedByUserId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, t)).First();
        }

        public async Task Update(EmailTemplate t)
        {
            const string sql = @"
                UPDATE email_template
                SET name = @Name, subject = @Subject, preview_text = @PreviewText, body_html = @BodyHtml, updated_at = now()
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, t);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            await _db.Execute("DELETE FROM email_template WHERE id = @id AND tenant_id = @tenantId", new { id, tenantId });
        }
    }
}
