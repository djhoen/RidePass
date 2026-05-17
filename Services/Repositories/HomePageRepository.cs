using Services.Helpers.Interfaces;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class HomePageRepository : IHomePageRepository
    {
        private readonly IDbHelper _db;

        public HomePageRepository(IDbHelper db) => _db = db;

        // ── Gallery ──────────────────────────────────────────────────────────────

        public async Task<List<TenantGalleryImage>> ListGallery(Guid tenantId)
        {
            const string sql = @"
                SELECT id, tenant_id AS TenantId, image_url AS ImageUrl, caption,
                       sort_order AS SortOrder, created_at AS CreatedAt
                FROM tenant_gallery_image
                WHERE tenant_id = @tenantId
                ORDER BY sort_order, created_at";
            return (await _db.Query<TenantGalleryImage>(sql, new { tenantId })).ToList();
        }

        public async Task<Guid> AddGalleryImage(Guid tenantId, string imageUrl, string? caption, int sortOrder)
        {
            const string sql = @"
                INSERT INTO tenant_gallery_image (tenant_id, image_url, caption, sort_order)
                VALUES (@tenantId, @imageUrl, @caption, @sortOrder)
                RETURNING id";
            return (await _db.Query<Guid>(sql, new { tenantId, imageUrl, caption, sortOrder })).First();
        }

        public async Task UpdateGalleryImage(Guid id, Guid tenantId, string? caption, int sortOrder)
        {
            const string sql = @"
                UPDATE tenant_gallery_image
                SET caption = @caption, sort_order = @sortOrder
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, caption, sortOrder });
        }

        public async Task DeleteGalleryImage(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM tenant_gallery_image WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task UpdateGallerySortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            const string sql = @"
                UPDATE tenant_gallery_image AS g
                SET sort_order = data.sort_order
                FROM (SELECT unnest(@ids::uuid[]) AS id,
                             unnest(@orders::int[]) AS sort_order) AS data
                WHERE g.id = data.id AND g.tenant_id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId,
                ids = ids.ToArray(),
                orders = sortOrders.ToArray(),
            });
        }

        // ── Track graphics ───────────────────────────────────────────────────────

        public async Task<List<TenantTrackGraphic>> ListTrackGraphics(Guid tenantId)
        {
            const string sql = @"
                SELECT id, tenant_id AS TenantId, image_url AS ImageUrl, title, description,
                       sort_order AS SortOrder, created_at AS CreatedAt
                FROM tenant_track_graphic
                WHERE tenant_id = @tenantId
                ORDER BY sort_order, created_at";
            return (await _db.Query<TenantTrackGraphic>(sql, new { tenantId })).ToList();
        }

        public async Task<Guid> AddTrackGraphic(Guid tenantId, string imageUrl, string? title, string? description, int sortOrder)
        {
            const string sql = @"
                INSERT INTO tenant_track_graphic (tenant_id, image_url, title, description, sort_order)
                VALUES (@tenantId, @imageUrl, @title, @description, @sortOrder)
                RETURNING id";
            return (await _db.Query<Guid>(sql, new { tenantId, imageUrl, title, description, sortOrder })).First();
        }

        public async Task UpdateTrackGraphic(Guid id, Guid tenantId, string? title, string? description, int sortOrder)
        {
            const string sql = @"
                UPDATE tenant_track_graphic
                SET title = @title, description = @description, sort_order = @sortOrder
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, title, description, sortOrder });
        }

        public async Task DeleteTrackGraphic(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM tenant_track_graphic WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task UpdateTrackGraphicSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            const string sql = @"
                UPDATE tenant_track_graphic AS t
                SET sort_order = data.sort_order
                FROM (SELECT unnest(@ids::uuid[]) AS id,
                             unnest(@orders::int[]) AS sort_order) AS data
                WHERE t.id = data.id AND t.tenant_id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId,
                ids = ids.ToArray(),
                orders = sortOrders.ToArray(),
            });
        }
    }
}
