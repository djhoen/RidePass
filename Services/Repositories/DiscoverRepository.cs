using Services.Helpers.Interfaces;
using Services.Repositories.Data.DiscoverData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class DiscoverRepository : IDiscoverRepository
    {
        private readonly IDbHelper _db;

        public DiscoverRepository(IDbHelper db) => _db = db;

        public async Task<List<TrackDiscoverRow>> SearchTracks(double? lat, double? lng, double? radiusKm, string? q, int limit = 50)
        {
            var qLike = string.IsNullOrWhiteSpace(q) ? null : $"%{q.Trim()}%";
            const string sql = @"
                WITH ranked AS (
                    SELECT t.id AS TenantId, t.subdomain, t.display_name AS DisplayName,
                           t.address_line AS AddressLine, t.city, t.region, t.postal_code AS PostalCode, t.country,
                           t.latitude, t.longitude, t.status, t.is_published,
                           tb.hero_image_url AS HeroImageUrl,
                           t.client_type AS ClientType, t.custom_domain AS CustomDomain,
                           t.custom_domain_verified AS CustomDomainVerified,
                           t.external_home_url AS ExternalHomeUrl, t.external_events_url AS ExternalEventsUrl,
                           CASE WHEN @lat::double precision IS NOT NULL
                                 AND @lng::double precision IS NOT NULL
                                 AND t.latitude IS NOT NULL AND t.longitude IS NOT NULL THEN
                               6371 * acos(GREATEST(-1, LEAST(1,
                                   cos(radians(@lat::double precision)) * cos(radians(t.latitude))
                                     * cos(radians(t.longitude) - radians(@lng::double precision))
                                   + sin(radians(@lat::double precision)) * sin(radians(t.latitude))
                               )))
                           END AS DistanceKm
                    FROM tenant t
                    LEFT JOIN tenant_branding tb ON tb.tenant_id = t.id
                )
                SELECT ranked.TenantId, ranked.Subdomain, ranked.DisplayName,
                       ranked.AddressLine, ranked.City, ranked.Region, ranked.PostalCode, ranked.Country,
                       ranked.Latitude, ranked.Longitude, ranked.DistanceKm,
                       ranked.HeroImageUrl,
                       ranked.ClientType, ranked.CustomDomain, ranked.CustomDomainVerified,
                       ranked.ExternalHomeUrl, ranked.ExternalEventsUrl,
                       (SELECT COUNT(*) FROM event e
                         WHERE e.tenant_id = ranked.TenantId
                           AND e.status = 'scheduled'
                           AND e.starts_at > NOW())::int AS UpcomingEventsCount
                FROM ranked
                WHERE ranked.status = 'active'
                  AND ranked.is_published
                  AND (@qLike::text IS NULL
                       OR ranked.DisplayName ILIKE @qLike
                       OR COALESCE(ranked.City,'') ILIKE @qLike
                       OR COALESCE(ranked.Region,'') ILIKE @qLike)
                  AND (@radiusKm::double precision IS NULL
                       OR (ranked.DistanceKm IS NOT NULL AND ranked.DistanceKm <= @radiusKm::double precision))
                ORDER BY ranked.DistanceKm NULLS LAST, ranked.DisplayName
                LIMIT @limit";
            var r = await _db.Query<TrackDiscoverRow>(sql, new { lat, lng, radiusKm, qLike, limit });
            return r.ToList();
        }

        public async Task<List<EventDiscoverRow>> SearchEvents(double? lat, double? lng, double? radiusKm, string? q,
            DateTime? fromUtc, DateTime? toUtc, string[]? eventTypeCodes = null, Guid[]? tenantIds = null,
            string[]? excludeCodes = null, int limit = 200)
        {
            var qLike = string.IsNullOrWhiteSpace(q) ? null : $"%{q.Trim()}%";
            // Empty arrays would filter everything out; treat them as "no filter".
            var codes = (eventTypeCodes is { Length: > 0 }) ? eventTypeCodes : null;
            var tenants = (tenantIds is { Length: > 0 }) ? tenantIds : null;
            // Deny-list (e.g. the apex page hides private bookings + lessons). These
            // events are never returned, so they don't leak to the public client.
            var excl = (excludeCodes is { Length: > 0 }) ? excludeCodes : null;
            const string sql = @"
                WITH ranked AS (
                    SELECT e.id AS EventId, e.tenant_id AS TenantId, e.title, e.starts_at AS StartsAtUtc,
                           e.ends_at AS EndsAtUtc, e.location_label AS LocationLabel,
                           e.image_url AS ImageUrl,
                           t.subdomain AS TenantSubdomain, t.display_name AS TenantDisplayName,
                           t.city AS TenantCity, t.region AS TenantRegion, t.status AS tenant_status,
                           t.is_published AS tenant_is_published,
                           t.latitude, t.longitude,
                           COALESCE(tb.logo_white_url, tb.logo_url) AS TenantLogoUrl,
                           t.client_type AS TenantClientType, t.custom_domain AS TenantCustomDomain,
                           t.custom_domain_verified AS TenantCustomDomainVerified,
                           t.external_home_url AS TenantExternalHomeUrl, t.external_events_url AS TenantExternalEventsUrl,
                           t.embed_event_target AS TenantEmbedEventTarget,
                           et.code AS EventTypeCode, et.name AS EventTypeName, et.color AS EventTypeColor,
                           et.image_url AS EventTypeImageUrl,
                           CASE WHEN @lat::double precision IS NOT NULL
                                 AND @lng::double precision IS NOT NULL
                                 AND t.latitude IS NOT NULL AND t.longitude IS NOT NULL THEN
                               6371 * acos(GREATEST(-1, LEAST(1,
                                   cos(radians(@lat::double precision)) * cos(radians(t.latitude))
                                     * cos(radians(t.longitude) - radians(@lng::double precision))
                                   + sin(radians(@lat::double precision)) * sin(radians(t.latitude))
                               )))
                           END AS DistanceKm
                    FROM event e
                    JOIN tenant t ON t.id = e.tenant_id
                    LEFT JOIN tenant_branding tb ON tb.tenant_id = t.id
                    JOIN tenant_event_type et ON et.id = e.event_type_id
                    WHERE e.status = 'scheduled'
                      AND (@codes::text[] IS NULL OR et.code = ANY(@codes))
                      AND (@excl::text[] IS NULL OR NOT (et.code = ANY(@excl)))
                      AND (@tenants::uuid[] IS NULL OR e.tenant_id = ANY(@tenants))
                )
                SELECT EventId, TenantId, TenantSubdomain, TenantDisplayName, TenantCity, TenantRegion,
                       TenantLogoUrl, TenantClientType, TenantCustomDomain, TenantCustomDomainVerified,
                       TenantExternalHomeUrl, TenantExternalEventsUrl, TenantEmbedEventTarget,
                       Latitude, Longitude, DistanceKm,
                       Title, StartsAtUtc, EndsAtUtc, LocationLabel,
                       EventTypeCode, EventTypeName, EventTypeColor,
                       ImageUrl, EventTypeImageUrl
                FROM ranked
                WHERE tenant_status = 'active'
                  AND tenant_is_published
                  AND StartsAtUtc >= COALESCE(@fromUtc::timestamptz, NOW())
                  AND (@toUtc::timestamptz IS NULL OR EndsAtUtc <= @toUtc::timestamptz)
                  AND (@qLike::text IS NULL
                       OR Title ILIKE @qLike
                       OR TenantDisplayName ILIKE @qLike
                       OR COALESCE(TenantCity,'') ILIKE @qLike
                       OR COALESCE(TenantRegion,'') ILIKE @qLike)
                  AND (@radiusKm::double precision IS NULL
                       OR (DistanceKm IS NOT NULL AND DistanceKm <= @radiusKm::double precision))
                ORDER BY StartsAtUtc ASC
                LIMIT @limit";
            var r = await _db.Query<EventDiscoverRow>(sql, new { lat, lng, radiusKm, qLike, fromUtc, toUtc, codes, excl, tenants, limit });
            return r.ToList();
        }

        public async Task<List<EventTypeOptionRow>> ListEventTypeOptions(string[]? onlyCodes = null, string[]? excludeCodes = null)
        {
            // Event types are per-tenant rows, but the system codes (open_ride,
            // race, practice, ...) are shared across every tenant. Collapse to one
            // row per code, picking the most common (name, color) for display.
            // Only codes attached to a scheduled, upcoming event at an active
            // tenant are returned, so the filter never lists a type with nothing
            // behind it. onlyCodes optionally restricts to an allow-list.
            var codes = (onlyCodes is { Length: > 0 }) ? onlyCodes : null;
            var excl = (excludeCodes is { Length: > 0 }) ? excludeCodes : null;
            const string sql = @"
                SELECT et.code AS Code,
                       MODE() WITHIN GROUP (ORDER BY et.name)  AS Name,
                       MODE() WITHIN GROUP (ORDER BY et.color) AS Color
                FROM tenant_event_type et
                JOIN tenant t ON t.id = et.tenant_id AND t.status = 'active' AND t.is_published
                WHERE (@codes::text[] IS NULL OR et.code = ANY(@codes))
                  AND (@excl::text[] IS NULL OR NOT (et.code = ANY(@excl)))
                  AND EXISTS (
                      SELECT 1 FROM event e
                      WHERE e.event_type_id = et.id
                        AND e.status = 'scheduled'
                        AND e.starts_at > NOW())
                GROUP BY et.code
                ORDER BY MIN(et.sort_order), Code";
            var r = await _db.Query<EventTypeOptionRow>(sql, new { codes, excl });
            return r.ToList();
        }
    }
}
