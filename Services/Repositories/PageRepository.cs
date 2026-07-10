using Services.Helpers.Interfaces;
using Services.Repositories.Data.PageData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class PageRepository : IPageRepository
    {
        private const string PageColumns = @"
            id, tenant_id AS TenantId, title, slug,
            body_html AS BodyHtml, hero_image_url AS HeroImageUrl,
            status, show_in_nav AS ShowInNav, nav_label AS NavLabel,
            sort_order AS SortOrder, published_at AS PublishedAt,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public PageRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<List<TenantPage>> ListAll(Guid tenantId, bool publishedOnly)
        {
            // Published first, then drafts (admin list); each group by sort_order. The
            // public list passes publishedOnly=true so only the published group shows.
            var sql = $@"
                SELECT {PageColumns}
                FROM tenant_page
                WHERE tenant_id = @tenantId
                  {(publishedOnly ? "AND status = 'published'" : "")}
                ORDER BY (status = 'published') DESC,
                         sort_order,
                         created_at DESC";
            var rows = await _db.Query<TenantPage>(sql, new { tenantId });
            return rows.ToList();
        }

        public async Task<TenantPage?> GetById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {PageColumns}
                FROM tenant_page
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var rows = await _db.Query<TenantPage>(sql, new { id, tenantId });
            return rows.FirstOrDefault();
        }

        public async Task<TenantPage?> GetBySlug(string slug, Guid tenantId, bool publishedOnly)
        {
            // Slugs are stored case-significant but matched case-insensitively, so a link
            // typed with different casing still resolves (and can't collide — see the
            // unique index on (tenant_id, lower(slug))).
            var sql = $@"
                SELECT {PageColumns}
                FROM tenant_page
                WHERE tenant_id = @tenantId AND lower(slug) = lower(@slug)
                  {(publishedOnly ? "AND status = 'published'" : "")}
                LIMIT 1";
            var rows = await _db.Query<TenantPage>(sql, new { tenantId, slug });
            return rows.FirstOrDefault();
        }

        public async Task<List<TenantPage>> ListNavPages(Guid tenantId)
        {
            var sql = $@"
                SELECT {PageColumns}
                FROM tenant_page
                WHERE tenant_id = @tenantId AND status = 'published' AND show_in_nav
                ORDER BY sort_order, created_at";
            var rows = await _db.Query<TenantPage>(sql, new { tenantId });
            return rows.ToList();
        }

        public async Task<bool> SlugExists(Guid tenantId, string slug, Guid? excludePageId)
        {
            const string sql = @"
                SELECT COUNT(*) FROM tenant_page
                WHERE tenant_id = @tenantId AND lower(slug) = lower(@slug)
                  AND (@excludePageId IS NULL OR id <> @excludePageId)";
            var count = await _db.ExecuteScalar(sql, new { tenantId, slug, excludePageId });
            return count > 0;
        }

        public async Task<Guid> Create(TenantPage page)
        {
            const string sql = @"
                INSERT INTO tenant_page (tenant_id, title, slug, body_html, hero_image_url,
                                         status, show_in_nav, nav_label, sort_order, published_at)
                VALUES (@TenantId, @Title, @Slug, @BodyHtml, @HeroImageUrl,
                        @Status, @ShowInNav, @NavLabel, @SortOrder, @PublishedAt)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, page);
            return result.First();
        }

        public async Task Update(TenantPage page)
        {
            const string sql = @"
                UPDATE tenant_page SET
                    title          = @Title,
                    slug           = @Slug,
                    body_html      = @BodyHtml,
                    hero_image_url = @HeroImageUrl,
                    status         = @Status,
                    show_in_nav    = @ShowInNav,
                    nav_label      = @NavLabel,
                    sort_order     = @SortOrder,
                    published_at   = @PublishedAt
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, page);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            await _db.Execute("DELETE FROM tenant_page WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId });
        }

        public async Task Reorder(Guid tenantId, IEnumerable<(Guid Id, int SortOrder)> order)
        {
            const string sql = @"
                UPDATE tenant_page SET sort_order = @sortOrder
                WHERE id = @id AND tenant_id = @tenantId";
            foreach (var (id, sortOrder) in order)
            {
                await _db.Execute(sql, new { id, sortOrder, tenantId });
            }
        }
    }
}
