using Services.Helpers.Interfaces;
using Services.Repositories.Data.MeData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    /// <summary>
    /// CROSS-TENANT BY DESIGN. The standard tenant audit rule (every query
    /// scoped by tenant_id = @tenantId) does NOT apply here: this is the apex
    /// landing page's "what's coming up for me" feed, deliberately spanning
    /// every tenant the rider has bought from. The scope predicate is the
    /// rider's user id (carried by the JWT and pinned at the controller),
    /// not a tenant id.
    ///
    /// Each branch:
    ///   • filters status = 'paid' (cancelled / refunded / pending rows
    ///     don't represent something the rider can still use).
    ///   • applies a kind-specific "still upcoming" filter so expired
    ///     entries fall off the list automatically.
    ///   • joins the tenant row so the response can carry the subdomain
    ///     (for "Visit track" links) and display name without an extra
    ///     round trip per row.
    ///
    /// Ordering: items with a concrete occurrence date first (sorted
    /// ascending by that date), then range-based entitlements (season
    /// passes, memberships) by purchase recency.
    /// </summary>
    public class UpcomingPurchaseRepository : IUpcomingPurchaseRepository
    {
        private readonly IDbHelper _db;

        public UpcomingPurchaseRepository(IDbHelper db) => _db = db;

        public async Task<List<UpcomingPurchaseRow>> ListForUser(Guid userId)
        {
            const string sql = @"
                -- Event tickets: future events the rider holds a paid tier
                -- ticket for. Joins through tier → event for the start time.
                SELECT
                    'event_ticket'::text                    AS Kind,
                    t.id                                    AS Id,
                    t.tenant_id                             AS TenantId,
                    ten.subdomain                           AS TenantSubdomain,
                    ten.display_name                        AS TenantDisplayName,
                    e.title                                 AS ItemName,
                    e.starts_at_utc                         AS OccursAtUtc,
                    NULL::timestamptz                       AS ValidToUtc,
                    t.amount_cents                          AS AmountCents,
                    t.redemption_token::text                AS RedemptionToken,
                    t.created_at                            AS CreatedAtUtc
                FROM event_ticket_purchase t
                JOIN event_ticket_tier tt ON tt.id = t.tier_id
                JOIN event e              ON e.id  = tt.event_id
                JOIN tenant ten           ON ten.id = t.tenant_id
                WHERE t.purchaser_user_id = @userId
                  AND t.status = 'paid'
                  AND e.starts_at_utc > now()

                UNION ALL

                -- Day passes: valid for a specific calendar date.
                SELECT
                    'pass'::text,
                    p.id,
                    p.tenant_id,
                    ten.subdomain,
                    ten.display_name,
                    pr.name,
                    p.valid_on_date::timestamptz            AS OccursAtUtc,
                    NULL::timestamptz,
                    p.amount_cents,
                    p.redemption_token::text,
                    p.created_at
                FROM pass_purchase p
                JOIN pass_product pr ON pr.id = p.product_id
                JOIN tenant ten      ON ten.id = p.tenant_id
                WHERE p.purchaser_user_id = @userId
                  AND p.status = 'paid'
                  AND p.valid_on_date >= current_date

                UNION ALL

                -- Season passes: valid through a date range. Listed while
                -- their valid_to_date is still in the future.
                SELECT
                    'season_pass'::text,
                    s.id,
                    s.tenant_id,
                    ten.subdomain,
                    ten.display_name,
                    sp.name,
                    NULL::timestamptz,
                    (s.valid_to_date + INTERVAL '1 day')::timestamptz AS ValidToUtc,
                    s.amount_cents,
                    s.redemption_token::text,
                    s.created_at
                FROM season_pass_purchase s
                JOIN season_pass_product sp ON sp.id = s.product_id
                JOIN tenant ten             ON ten.id = s.tenant_id
                WHERE s.purchaser_user_id = @userId
                  AND s.status = 'paid'
                  AND s.valid_to_date >= current_date

                UNION ALL

                -- Memberships: user_id (not purchaser_user_id) on this table.
                -- valid_to_utc is nullable (lifetime memberships), so include
                -- rows where it's null or in the future.
                SELECT
                    'membership'::text,
                    m.id,
                    m.tenant_id,
                    ten.subdomain,
                    ten.display_name,
                    m.name_at_purchase,
                    NULL::timestamptz,
                    m.valid_to_utc,
                    m.amount_cents,
                    NULL::text,
                    m.created_at
                FROM membership_purchase m
                JOIN tenant ten ON ten.id = m.tenant_id
                WHERE m.user_id = @userId
                  AND m.status = 'paid'
                  AND (m.valid_to_utc IS NULL OR m.valid_to_utc > now())

                ORDER BY OccursAtUtc NULLS LAST, CreatedAtUtc DESC";

            var rows = await _db.Query<UpcomingPurchaseRow>(sql, new { userId });
            return rows.ToList();
        }
    }
}
