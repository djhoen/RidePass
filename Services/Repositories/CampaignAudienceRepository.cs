using Services.Helpers.Interfaces;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class CampaignAudienceRepository : ICampaignAudienceRepository
    {
        private readonly IDbHelper _db;
        private readonly IAudienceRepository _savedAudiences;

        public CampaignAudienceRepository(IDbHelper db, IAudienceRepository savedAudiences)
        {
            _db = db;
            _savedAudiences = savedAudiences;
        }

        public async Task<List<CampaignAudienceRecipient>> ListRecipients(Guid tenantId, string kind, CampaignAudienceConfig config)
        {
            // Emails are lower-cased in SQL so DISTINCT ON collapses case variants of the same
            // inbox, and so the suppression blocklist (also lower-cased) matches.
            switch (kind)
            {
                case CampaignAudienceKinds.Subscribers:
                {
                    const string sql = @"
                        SELECT DISTINCT ON (lower(email))
                               lower(email) AS Email, name AS Name, id AS SubscriberId
                        FROM newsletter_subscriber
                        WHERE tenant_id = @tenantId AND unsubscribed_at IS NULL
                        ORDER BY lower(email), subscribed_at DESC";
                    var r = await _db.Query<CampaignAudienceRecipient>(sql, new { tenantId });
                    return r.ToList();
                }
                case CampaignAudienceKinds.Event:
                {
                    if (config.EventId is null) return new List<CampaignAudienceRecipient>();
                    // 'redeemed' is a paid ticket that was scanned at the gate; still a purchaser.
                    const string sql = @"
                        SELECT DISTINCT ON (lower(p.purchaser_email))
                               lower(p.purchaser_email) AS Email, p.purchaser_name AS Name, NULL::uuid AS SubscriberId
                        FROM event_ticket_purchase p
                        JOIN event_ticket_tier t ON t.id = p.tier_id
                        JOIN event e ON e.id = t.event_id AND e.tenant_id = p.tenant_id
                        WHERE p.tenant_id = @tenantId
                          AND p.status IN ('paid', 'redeemed')
                          AND e.id = @eventId
                        ORDER BY lower(p.purchaser_email), p.created_at DESC";
                    var r = await _db.Query<CampaignAudienceRecipient>(sql, new { tenantId, eventId = config.EventId });
                    return r.ToList();
                }
                case CampaignAudienceKinds.EventType:
                {
                    if (config.EventTypeId is null) return new List<CampaignAudienceRecipient>();
                    const string sql = @"
                        SELECT DISTINCT ON (lower(p.purchaser_email))
                               lower(p.purchaser_email) AS Email, p.purchaser_name AS Name, NULL::uuid AS SubscriberId
                        FROM event_ticket_purchase p
                        JOIN event_ticket_tier t ON t.id = p.tier_id
                        JOIN event e ON e.id = t.event_id AND e.tenant_id = p.tenant_id
                        WHERE p.tenant_id = @tenantId
                          AND p.status IN ('paid', 'redeemed')
                          AND e.event_type_id = @eventTypeId
                          AND (CAST(@fromUtc AS timestamptz) IS NULL OR e.starts_at >= CAST(@fromUtc AS timestamptz))
                          AND (CAST(@toUtc   AS timestamptz) IS NULL OR e.starts_at <  CAST(@toUtc   AS timestamptz))
                        ORDER BY lower(p.purchaser_email), p.created_at DESC";
                    var r = await _db.Query<CampaignAudienceRecipient>(sql,
                        new { tenantId, eventTypeId = config.EventTypeId, fromUtc = config.FromUtc, toUtc = config.ToUtc });
                    return r.ToList();
                }
                case CampaignAudienceKinds.PassProduct:
                {
                    if (config.PassProductId is null) return new List<CampaignAudienceRecipient>();
                    const string sql = @"
                        SELECT DISTINCT ON (lower(purchaser_email))
                               lower(purchaser_email) AS Email, purchaser_name AS Name, NULL::uuid AS SubscriberId
                        FROM season_pass_purchase
                        WHERE tenant_id = @tenantId
                          AND product_id = @productId
                          AND status = 'paid'
                        ORDER BY lower(purchaser_email), created_at DESC";
                    var r = await _db.Query<CampaignAudienceRecipient>(sql, new { tenantId, productId = config.PassProductId });
                    return r.ToList();
                }
                case CampaignAudienceKinds.Audience:
                {
                    // Evaluated live, so the send reaches whoever matches at send time.
                    if (config.AudienceId is null) return new List<CampaignAudienceRecipient>();
                    var audience = await _savedAudiences.GetById(config.AudienceId.Value, tenantId);
                    if (audience is null) return new List<CampaignAudienceRecipient>();
                    var people = await _savedAudiences.Evaluate(tenantId, AudienceDefinition.Parse(audience.Definition));
                    return people.Select(p => new CampaignAudienceRecipient(p.Email, p.Name, null)).ToList();
                }
                default:
                    return new List<CampaignAudienceRecipient>();
            }
        }

        public async Task<CampaignAudienceOptions> ListOptions(Guid tenantId)
        {
            // Two seasons of events is enough for "everyone who came to X"; cap keeps the picker sane.
            const string eventsSql = @"
                SELECT e.id AS Id, e.title AS Title, e.starts_at AS StartsAt, e.status AS Status,
                       et.name AS EventTypeName
                FROM event e
                JOIN tenant_event_type et ON et.id = e.event_type_id
                WHERE e.tenant_id = @tenantId
                  AND e.starts_at >= now() - interval '24 months'
                ORDER BY e.starts_at DESC
                LIMIT 500";
            const string typesSql = @"
                SELECT id AS Id, name AS Name, true AS IsActive
                FROM tenant_event_type
                WHERE tenant_id = @tenantId
                ORDER BY sort_order, name";
            const string productsSql = @"
                SELECT id AS Id, name AS Name, is_active AS IsActive
                FROM season_pass_product
                WHERE tenant_id = @tenantId
                ORDER BY is_active DESC, name";
            var events = await _db.Query<CampaignAudienceEventOption>(eventsSql, new { tenantId });
            var types = await _db.Query<CampaignAudienceNamedOption>(typesSql, new { tenantId });
            var products = await _db.Query<CampaignAudienceNamedOption>(productsSql, new { tenantId });
            return new CampaignAudienceOptions
            {
                Events = events.ToList(),
                EventTypes = types.ToList(),
                PassProducts = products.ToList(),
            };
        }

        public async Task<string?> TargetName(Guid tenantId, string kind, CampaignAudienceConfig config)
        {
            switch (kind)
            {
                case CampaignAudienceKinds.Event:
                    if (config.EventId is null) return null;
                    return (await _db.Query<string>("SELECT title FROM event WHERE id = @id AND tenant_id = @tenantId",
                        new { id = config.EventId, tenantId })).FirstOrDefault();
                case CampaignAudienceKinds.EventType:
                    if (config.EventTypeId is null) return null;
                    return (await _db.Query<string>("SELECT name FROM tenant_event_type WHERE id = @id AND tenant_id = @tenantId",
                        new { id = config.EventTypeId, tenantId })).FirstOrDefault();
                case CampaignAudienceKinds.PassProduct:
                    if (config.PassProductId is null) return null;
                    return (await _db.Query<string>("SELECT name FROM season_pass_product WHERE id = @id AND tenant_id = @tenantId",
                        new { id = config.PassProductId, tenantId })).FirstOrDefault();
                case CampaignAudienceKinds.Audience:
                    if (config.AudienceId is null) return null;
                    return (await _savedAudiences.GetById(config.AudienceId.Value, tenantId))?.Name;
                default:
                    return null;
            }
        }

        public async Task<string?> DescribeAudience(Guid tenantId, string kind, CampaignAudienceConfig config)
        {
            switch (kind)
            {
                case CampaignAudienceKinds.Subscribers:
                    return "Newsletter subscribers";
                case CampaignAudienceKinds.Event:
                {
                    if (config.EventId is null) return null;
                    var r = await _db.Query<string>(
                        "SELECT title FROM event WHERE id = @id AND tenant_id = @tenantId",
                        new { id = config.EventId, tenantId });
                    var title = r.FirstOrDefault();
                    return title is null ? null : $"Purchasers of {title}";
                }
                case CampaignAudienceKinds.EventType:
                {
                    if (config.EventTypeId is null) return null;
                    var r = await _db.Query<string>(
                        "SELECT name FROM tenant_event_type WHERE id = @id AND tenant_id = @tenantId",
                        new { id = config.EventTypeId, tenantId });
                    var name = r.FirstOrDefault();
                    if (name is null) return null;
                    var window = (config.FromUtc, config.ToUtc) switch
                    {
                        (not null, not null) => $" ({config.FromUtc:MMM d, yyyy} to {config.ToUtc:MMM d, yyyy})",
                        (not null, null) => $" (from {config.FromUtc:MMM d, yyyy})",
                        (null, not null) => $" (before {config.ToUtc:MMM d, yyyy})",
                        _ => string.Empty,
                    };
                    return $"Purchasers of any {name}{window}";
                }
                case CampaignAudienceKinds.PassProduct:
                {
                    if (config.PassProductId is null) return null;
                    var r = await _db.Query<string>(
                        "SELECT name FROM season_pass_product WHERE id = @id AND tenant_id = @tenantId",
                        new { id = config.PassProductId, tenantId });
                    var name = r.FirstOrDefault();
                    return name is null ? null : $"Holders of {name}";
                }
                case CampaignAudienceKinds.Audience:
                {
                    if (config.AudienceId is null) return null;
                    return (await _savedAudiences.GetById(config.AudienceId.Value, tenantId))?.Name;
                }
                default:
                    return null;
            }
        }
    }
}
