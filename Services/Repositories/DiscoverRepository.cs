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
                           t.latitude, t.longitude, t.status,
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
                )
                SELECT ranked.TenantId, ranked.Subdomain, ranked.DisplayName,
                       ranked.AddressLine, ranked.City, ranked.Region, ranked.PostalCode, ranked.Country,
                       ranked.Latitude, ranked.Longitude, ranked.DistanceKm,
                       (SELECT COUNT(*) FROM event e
                         WHERE e.tenant_id = ranked.TenantId
                           AND e.status = 'scheduled'
                           AND e.starts_at > NOW())::int AS UpcomingEventsCount
                FROM ranked
                WHERE ranked.status = 'active'
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
            DateTime? fromUtc, DateTime? toUtc, int limit = 100)
        {
            var qLike = string.IsNullOrWhiteSpace(q) ? null : $"%{q.Trim()}%";
            const string sql = @"
                WITH ranked AS (
                    SELECT e.id AS EventId, e.tenant_id AS TenantId, e.title, e.starts_at AS StartsAtUtc,
                           e.ends_at AS EndsAtUtc, e.location_label AS LocationLabel,
                           t.subdomain AS TenantSubdomain, t.display_name AS TenantDisplayName,
                           t.city AS TenantCity, t.region AS TenantRegion, t.status AS tenant_status,
                           t.latitude, t.longitude,
                           et.name AS EventTypeName, et.color AS EventTypeColor,
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
                    JOIN tenant_event_type et ON et.id = e.event_type_id
                    WHERE e.status = 'scheduled'
                )
                SELECT EventId, TenantId, TenantSubdomain, TenantDisplayName, TenantCity, TenantRegion,
                       Latitude, Longitude, DistanceKm,
                       Title, StartsAtUtc, EndsAtUtc, LocationLabel,
                       EventTypeName, EventTypeColor
                FROM ranked
                WHERE tenant_status = 'active'
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
            var r = await _db.Query<EventDiscoverRow>(sql, new { lat, lng, radiusKm, qLike, fromUtc, toUtc, limit });
            return r.ToList();
        }
    }
}
