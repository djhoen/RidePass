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
                -- Event tickets: ONE row per event the rider holds paid tickets for
                -- (race entries + gate fees collapse into a single event card). Past +
                -- future both come back; the UI splits them with a toggle. Cover image
                -- is the event's, falling back to its event-type default.
                SELECT
                    'event_ticket'::text                    AS Kind,
                    e.id                                    AS Id,
                    t.tenant_id                             AS TenantId,
                    ten.subdomain                           AS TenantSubdomain,
                    ten.display_name                        AS TenantDisplayName,
                    e.title                                 AS ItemName,
                    COALESCE(e.image_url, et.image_url)     AS ImageUrl,
                    COALESCE(tb.logo_white_url, tb.logo_url) AS TenantLogoUrl,
                    bool_and(t.registration_complete)       AS RegistrationComplete,
                    -- Signed = the normalized signature link, or either legacy inline copy. Reading
                    -- only waiver_signed_at made every ticket signed through the current registration
                    -- flow (which sets waiver_signature_id) report back as UNSIGNED on the rider's card.
                    bool_or(t.waiver_signature_id IS NOT NULL
                            OR t.waiver_signed_at IS NOT NULL
                            OR t.waiver_signature_data_url IS NOT NULL) AS WaiverSigned,
                    e.starts_at                             AS OccursAtUtc,
                    e.ends_at                               AS EndsAtUtc,
                    NULL::timestamptz                       AS ValidToUtc,
                    SUM(t.amount_cents)::int                AS AmountCents,
                    MIN(t.redemption_token::text)           AS RedemptionToken,
                    MIN(t.created_at)                       AS CreatedAtUtc
                FROM event_ticket_purchase t
                JOIN event_ticket_tier tt ON tt.id = t.tier_id
                JOIN event e              ON e.id  = tt.event_id
                JOIN tenant_event_type et ON et.id = e.event_type_id
                JOIN tenant ten           ON ten.id = t.tenant_id
                LEFT JOIN tenant_branding tb ON tb.tenant_id = ten.id
                WHERE t.purchaser_user_id = @userId
                  -- 'redeemed' = checked in at the gate, which is the one moment a rider is MOST
                  -- likely to open this page. Filtering it out made the event vanish from their
                  -- card the instant they were scanned in, and took the past-events history with it.
                  AND t.status IN ('paid', 'redeemed')
                GROUP BY e.id, e.title, e.starts_at, e.image_url, et.image_url,
                         tb.logo_white_url, tb.logo_url,
                         t.tenant_id, ten.subdomain, ten.display_name

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
                    NULL::text,
                    NULL::text,
                    true,
                    false,
                    NULL::timestamptz,
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
                    NULL::text,
                    NULL::text,
                    true,
                    false,
                    NULL::timestamptz,
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
