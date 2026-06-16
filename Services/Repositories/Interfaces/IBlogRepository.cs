using Services.Repositories.Data.BlogData;

namespace Services.Repositories.Interfaces
{
    public interface IBlogRepository
    {
        // ── Posts ──
        /// <summary>All posts for the tenant. publishedOnly=true is the public list (published only).</summary>
        Task<List<BlogPost>> ListForTenant(Guid tenantId, bool publishedOnly);
        Task<BlogPost?> GetById(Guid id, Guid tenantId);
        Task<BlogPost?> GetBySlug(string slug, Guid tenantId, bool publishedOnly);
        /// <summary>The tenant's featured + published post, if any (the home-page feature slot).</summary>
        Task<BlogPost?> GetFeatured(Guid tenantId);
        /// <summary>True if another post in this tenant already owns the slug (case-insensitive).</summary>
        Task<bool> SlugExists(Guid tenantId, string slug, Guid? excludePostId);
        Task<Guid> Create(BlogPost post);
        Task Update(BlogPost post);
        Task Delete(Guid id, Guid tenantId);
        /// <summary>Feature (or unfeature) a post, clearing any prior featured post first.</summary>
        Task SetFeatured(Guid id, Guid tenantId, bool featured);

        // ── Images (the "several other images") ──
        Task<List<BlogPostImage>> ListImages(Guid blogPostId, Guid tenantId);
        Task<Dictionary<Guid, List<BlogPostImage>>> ListImagesForPosts(IEnumerable<Guid> postIds, Guid tenantId);
        Task<Guid> AddImage(BlogPostImage image);
        Task<BlogPostImage?> GetImage(Guid imageId, Guid tenantId);
        Task UpdateImageCaption(Guid imageId, Guid tenantId, string? caption);
        Task DeleteImage(Guid imageId, Guid tenantId);
        Task ReorderImages(Guid blogPostId, Guid tenantId, IEnumerable<(Guid Id, int SortOrder)> order);
    }
}
