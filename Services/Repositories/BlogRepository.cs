using Services.Helpers.Interfaces;
using Services.Repositories.Data.BlogData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class BlogRepository : IBlogRepository
    {
        private const string PostColumns = @"
            id, tenant_id AS TenantId, title, slug, excerpt,
            body_html AS BodyHtml, main_image_url AS MainImageUrl,
            status, is_featured AS IsFeatured, published_at AS PublishedAt,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string ImageColumns = @"
            id, blog_post_id AS BlogPostId, tenant_id AS TenantId,
            image_url AS ImageUrl, caption, sort_order AS SortOrder,
            created_at AS CreatedAt";

        private readonly IDbHelper _db;

        public BlogRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<List<BlogPost>> ListForTenant(Guid tenantId, bool publishedOnly)
        {
            // Published first, then drafts (admin list); each group newest-first. The
            // public list passes publishedOnly=true so only the published group shows.
            var sql = $@"
                SELECT {PostColumns}
                FROM blog_post
                WHERE tenant_id = @tenantId
                  {(publishedOnly ? "AND status = 'published'" : "")}
                ORDER BY (status = 'published') DESC,
                         published_at DESC NULLS LAST,
                         created_at DESC";
            var rows = await _db.Query<BlogPost>(sql, new { tenantId });
            return rows.ToList();
        }

        public async Task<BlogPost?> GetById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {PostColumns}
                FROM blog_post
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var rows = await _db.Query<BlogPost>(sql, new { id, tenantId });
            return rows.FirstOrDefault();
        }

        public async Task<BlogPost?> GetBySlug(string slug, Guid tenantId, bool publishedOnly)
        {
            // Slugs are stored case-significant but matched case-insensitively, so a link
            // typed with different casing still resolves (and can't collide — see the
            // unique index on (tenant_id, lower(slug))).
            var sql = $@"
                SELECT {PostColumns}
                FROM blog_post
                WHERE tenant_id = @tenantId AND lower(slug) = lower(@slug)
                  {(publishedOnly ? "AND status = 'published'" : "")}
                LIMIT 1";
            var rows = await _db.Query<BlogPost>(sql, new { tenantId, slug });
            return rows.FirstOrDefault();
        }

        public async Task<BlogPost?> GetFeatured(Guid tenantId)
        {
            var sql = $@"
                SELECT {PostColumns}
                FROM blog_post
                WHERE tenant_id = @tenantId AND is_featured AND status = 'published'
                LIMIT 1";
            var rows = await _db.Query<BlogPost>(sql, new { tenantId });
            return rows.FirstOrDefault();
        }

        public async Task<bool> SlugExists(Guid tenantId, string slug, Guid? excludePostId)
        {
            const string sql = @"
                SELECT COUNT(*) FROM blog_post
                WHERE tenant_id = @tenantId AND lower(slug) = lower(@slug)
                  AND (@excludePostId IS NULL OR id <> @excludePostId)";
            var count = await _db.ExecuteScalar(sql, new { tenantId, slug, excludePostId });
            return count > 0;
        }

        public async Task<Guid> Create(BlogPost post)
        {
            const string sql = @"
                INSERT INTO blog_post (tenant_id, title, slug, excerpt, body_html,
                                       main_image_url, status, published_at)
                VALUES (@TenantId, @Title, @Slug, @Excerpt, @BodyHtml,
                        @MainImageUrl, @Status, @PublishedAt)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, post);
            return result.First();
        }

        public async Task Update(BlogPost post)
        {
            // is_featured is included so unpublishing can clear it in one write; switching
            // the featured post on/off otherwise goes through SetFeatured.
            const string sql = @"
                UPDATE blog_post SET
                    title          = @Title,
                    slug           = @Slug,
                    excerpt        = @Excerpt,
                    body_html      = @BodyHtml,
                    main_image_url = @MainImageUrl,
                    status         = @Status,
                    is_featured    = @IsFeatured,
                    published_at   = @PublishedAt
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, post);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            // blog_post_image cascades via FK.
            await _db.Execute("DELETE FROM blog_post WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId });
        }

        public async Task SetFeatured(Guid id, Guid tenantId, bool featured)
        {
            if (featured)
            {
                // Only one featured post per tenant — clear the current holder first so the
                // partial unique index (tenant_id WHERE is_featured) doesn't reject the switch.
                await _db.Execute(
                    "UPDATE blog_post SET is_featured = false WHERE tenant_id = @tenantId AND is_featured AND id <> @id",
                    new { tenantId, id });
            }
            await _db.Execute(
                "UPDATE blog_post SET is_featured = @featured WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId, featured });
        }

        public async Task<List<BlogPostImage>> ListImages(Guid blogPostId, Guid tenantId)
        {
            var sql = $@"
                SELECT {ImageColumns}
                FROM blog_post_image
                WHERE blog_post_id = @blogPostId AND tenant_id = @tenantId
                ORDER BY sort_order, created_at";
            var rows = await _db.Query<BlogPostImage>(sql, new { blogPostId, tenantId });
            return rows.ToList();
        }

        public async Task<Dictionary<Guid, List<BlogPostImage>>> ListImagesForPosts(IEnumerable<Guid> postIds, Guid tenantId)
        {
            var ids = postIds.ToArray();
            if (ids.Length == 0) return new Dictionary<Guid, List<BlogPostImage>>();
            var sql = $@"
                SELECT {ImageColumns}
                FROM blog_post_image
                WHERE tenant_id = @tenantId AND blog_post_id = ANY(@ids)
                ORDER BY sort_order, created_at";
            var rows = await _db.Query<BlogPostImage>(sql, new { tenantId, ids });
            return rows.GroupBy(r => r.BlogPostId).ToDictionary(g => g.Key, g => g.ToList());
        }

        public async Task<Guid> AddImage(BlogPostImage image)
        {
            const string sql = @"
                INSERT INTO blog_post_image (blog_post_id, tenant_id, image_url, caption, sort_order)
                VALUES (@BlogPostId, @TenantId, @ImageUrl, @Caption, @SortOrder)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, image);
            return result.First();
        }

        public async Task<BlogPostImage?> GetImage(Guid imageId, Guid tenantId)
        {
            var sql = $@"
                SELECT {ImageColumns}
                FROM blog_post_image
                WHERE id = @imageId AND tenant_id = @tenantId
                LIMIT 1";
            var rows = await _db.Query<BlogPostImage>(sql, new { imageId, tenantId });
            return rows.FirstOrDefault();
        }

        public async Task UpdateImageCaption(Guid imageId, Guid tenantId, string? caption)
        {
            await _db.Execute(
                "UPDATE blog_post_image SET caption = @caption WHERE id = @imageId AND tenant_id = @tenantId",
                new { imageId, tenantId, caption });
        }

        public async Task DeleteImage(Guid imageId, Guid tenantId)
        {
            await _db.Execute("DELETE FROM blog_post_image WHERE id = @imageId AND tenant_id = @tenantId",
                new { imageId, tenantId });
        }

        public async Task ReorderImages(Guid blogPostId, Guid tenantId, IEnumerable<(Guid Id, int SortOrder)> order)
        {
            const string sql = @"
                UPDATE blog_post_image SET sort_order = @sortOrder
                WHERE id = @id AND blog_post_id = @blogPostId AND tenant_id = @tenantId";
            foreach (var (id, sortOrder) in order)
            {
                await _db.Execute(sql, new { id, sortOrder, blogPostId, tenantId });
            }
        }
    }
}
