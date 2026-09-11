using Services.Helpers.Interfaces;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class MarketingReportRepository : IMarketingReportRepository
    {
        private readonly IDbHelper _db;

        public MarketingReportRepository(IDbHelper db) => _db = db;

        /// <summary>Every paid purchase of this tenant, by the buyer's email, with what it was worth.</summary>
        private const string Buys = @"
            SELECT lower(p.purchaser_email) AS email, p.created_at, p.amount_cents
            FROM event_ticket_purchase p
            WHERE p.tenant_id = @tenantId AND p.status IN ('paid', 'redeemed')
            UNION ALL
            SELECT lower(sp.purchaser_email), sp.created_at, sp.amount_cents
            FROM season_pass_purchase sp
            WHERE sp.tenant_id = @tenantId AND sp.status = 'paid'";

        /// <summary>This campaign's send rows, tenant enforced through the campaign, not trusted.</summary>
        private const string CampaignSends = @"
            SELECT s.id, lower(s.email) AS email, s.name, s.channel, s.status, s.error, s.sent_at
            FROM email_campaign_send s
            JOIN email_campaign c ON c.id = s.campaign_id AND c.tenant_id = @tenantId
            WHERE s.campaign_id = @campaignId";

        public async Task<CampaignReportTotals> GetCampaignTotals(Guid campaignId, Guid tenantId, int windowDays)
        {
            var sql = $@"
                WITH sends AS ({CampaignSends}),
                people AS (
                    SELECT email, MIN(sent_at) AS sent_at FROM sends WHERE status = 'sent' GROUP BY email
                ),
                clicks AS (
                    SELECT s.email, MIN(e.occurred_at) AS first_click
                    FROM email_engagement e
                    JOIN sends s ON s.id = e.source_send_id
                    WHERE e.tenant_id = @tenantId AND e.source_kind = 'campaign' AND e.event = 'click'
                    GROUP BY s.email
                ),
                buys AS ({Buys}),
                conv AS (
                    SELECT pe.email,
                           SUM(b.amount_cents) AS revenue,
                           BOOL_OR(cl.first_click IS NOT NULL AND cl.first_click <= b.created_at) AS after_click
                    FROM people pe
                    JOIN buys b ON b.email = pe.email
                                AND b.created_at >= pe.sent_at
                                AND b.created_at < pe.sent_at + make_interval(days => @days)
                    LEFT JOIN clicks cl ON cl.email = pe.email
                    GROUP BY pe.email
                )
                SELECT
                    (SELECT COUNT(*)::int FROM sends WHERE status = 'sent')                        AS Delivered,
                    (SELECT COUNT(*)::int FROM sends WHERE status = 'sent' AND channel = 'email')  AS Emails,
                    (SELECT COUNT(*)::int FROM sends WHERE status = 'sent' AND channel = 'sms')    AS Texts,
                    (SELECT COUNT(*)::int FROM sends WHERE status = 'skipped')                     AS Skipped,
                    (SELECT COUNT(*)::int FROM sends WHERE status = 'failed')                      AS Failed,
                    (SELECT COUNT(*)::int FROM people)                                             AS People,
                    (SELECT COUNT(DISTINCT s.email)::int
                     FROM email_engagement e JOIN sends s ON s.id = e.source_send_id
                     WHERE e.tenant_id = @tenantId AND e.source_kind = 'campaign' AND e.event = 'open') AS UniqueOpens,
                    (SELECT COUNT(*)::int FROM clicks)                                             AS UniqueClicks,
                    (SELECT COUNT(*)::int FROM conv)                                               AS Conversions,
                    (SELECT COUNT(*)::int FROM conv WHERE after_click)                             AS ClickConversions,
                    (SELECT COALESCE(SUM(revenue), 0)::bigint FROM conv)                           AS RevenueCents";
            return (await _db.Query<CampaignReportTotals>(sql, new { campaignId, tenantId, days = windowDays })).First();
        }

        public async Task<List<CampaignRecipientRow>> ListCampaignRecipients(Guid campaignId, Guid tenantId, int windowDays,
            string? search, string filter, int skip, int take)
        {
            // Opens, clicks, and purchases are each grouped once and joined by send id / email
            // rather than looked up per row: a 30k-recipient campaign stays a few hash joins.
            var sql = $@"
                WITH sends AS ({CampaignSends}),
                opens AS (
                    SELECT e.source_send_id AS send_id, MIN(e.occurred_at) AS first_open
                    FROM email_engagement e JOIN sends s ON s.id = e.source_send_id
                    WHERE e.tenant_id = @tenantId AND e.source_kind = 'campaign' AND e.event = 'open'
                    GROUP BY e.source_send_id
                ),
                clicks AS (
                    SELECT e.source_send_id AS send_id, MIN(e.occurred_at) AS first_click
                    FROM email_engagement e JOIN sends s ON s.id = e.source_send_id
                    WHERE e.tenant_id = @tenantId AND e.source_kind = 'campaign' AND e.event = 'click'
                    GROUP BY e.source_send_id
                ),
                buys AS ({Buys}),
                bought AS (
                    SELECT s.id AS send_id, MIN(b.created_at) AS first_buy, SUM(b.amount_cents) AS revenue
                    FROM sends s
                    JOIN buys b ON b.email = s.email
                                AND s.sent_at IS NOT NULL
                                AND b.created_at >= s.sent_at
                                AND b.created_at < s.sent_at + make_interval(days => @days)
                    GROUP BY s.id
                ),
                rows AS (
                    SELECT s.id, s.email, s.name, s.channel, s.status, s.error, s.sent_at,
                           o.first_open, c.first_click, b.first_buy, b.revenue
                    FROM sends s
                    LEFT JOIN opens o ON o.send_id = s.id
                    LEFT JOIN clicks c ON c.send_id = s.id
                    LEFT JOIN bought b ON b.send_id = s.id
                    WHERE (CAST(@search AS text) IS NULL OR s.email ILIKE @like OR s.name ILIKE @like)
                      AND (@filter = 'all'
                           OR (@filter = 'opened'  AND o.first_open IS NOT NULL)
                           OR (@filter = 'clicked' AND c.first_click IS NOT NULL)
                           OR (@filter = 'bought'  AND b.first_buy IS NOT NULL)
                           OR (@filter = 'skipped' AND s.status IN ('skipped', 'failed')))
                )
                SELECT id AS Id, email AS Email, name AS Name, channel AS Channel, status AS Status, error AS Error,
                       sent_at AS SentAt, first_open AS FirstOpenAt, first_click AS FirstClickAt,
                       first_buy AS FirstBuyAt, revenue AS RevenueCents,
                       COUNT(*) OVER()::int AS Total
                FROM rows
                ORDER BY first_buy DESC NULLS LAST, first_click DESC NULLS LAST, first_open DESC NULLS LAST, email
                LIMIT @take OFFSET @skip";
            var like = string.IsNullOrWhiteSpace(search) ? null : "%" + search.Trim() + "%";
            return (await _db.Query<CampaignRecipientRow>(sql, new
            {
                campaignId, tenantId, days = windowDays,
                search = like is null ? null : search!.Trim(), like, filter, skip, take,
            })).ToList();
        }

        public async Task<Dictionary<Guid, MarketingConversion>> GetCampaignConversions(Guid tenantId, int windowDays)
        {
            var sql = $@"
                WITH sends AS (
                    SELECT c.id AS campaign_id, lower(s.email) AS email, MIN(s.sent_at) AS sent_at
                    FROM email_campaign_send s
                    JOIN email_campaign c ON c.id = s.campaign_id
                    WHERE c.tenant_id = @tenantId AND s.status = 'sent'
                    GROUP BY c.id, lower(s.email)
                ),
                buys AS ({Buys})
                SELECT s.campaign_id AS Key,
                       COUNT(DISTINCT s.email)::int AS Conversions,
                       COALESCE(SUM(b.amount_cents), 0)::bigint AS RevenueCents
                FROM sends s
                JOIN buys b ON b.email = s.email
                            AND b.created_at >= s.sent_at
                            AND b.created_at < s.sent_at + make_interval(days => @days)
                GROUP BY s.campaign_id";
            var rows = await _db.Query<MarketingConversion>(sql, new { tenantId, days = windowDays });
            return rows.ToDictionary(r => r.Key);
        }

        public async Task<Dictionary<Guid, MarketingConversion>> GetAutomationStepConversions(Guid automationId, Guid tenantId, int windowDays)
        {
            var sql = $@"
                WITH sends AS (
                    SELECT ms.step_id, lower(ms.email) AS email, MIN(ms.sent_at) AS sent_at
                    FROM marketing_automation_send ms
                    WHERE ms.tenant_id = @tenantId AND ms.automation_id = @automationId AND ms.status = 'sent'
                    GROUP BY ms.step_id, lower(ms.email)
                ),
                buys AS ({Buys})
                SELECT s.step_id AS Key,
                       COUNT(DISTINCT s.email)::int AS Conversions,
                       COALESCE(SUM(b.amount_cents), 0)::bigint AS RevenueCents
                FROM sends s
                JOIN buys b ON b.email = s.email
                            AND b.created_at >= s.sent_at
                            AND b.created_at < s.sent_at + make_interval(days => @days)
                GROUP BY s.step_id";
            var rows = await _db.Query<MarketingConversion>(sql, new { automationId, tenantId, days = windowDays });
            return rows.ToDictionary(r => r.Key);
        }
    }
}
