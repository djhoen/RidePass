using Services.Repositories.Data.PageData;

namespace Services.Repositories.Interfaces
{
    public interface IPageRepository
    {
        /// <summary>All pages for the tenant. publishedOnly=true is the public list (published only).</summary>
        Task<List<TenantPage>> ListAll(Guid tenantId, bool publishedOnly);
        Task<TenantPage?> GetById(Guid id, Guid tenantId);
        Task<TenantPage?> GetBySlug(string slug, Guid tenantId, bool publishedOnly);
        /// <summary>Published + nav-visible pages for the tenant, ordered by sort_order (drives the public nav).</summary>
        Task<List<TenantPage>> ListNavPages(Guid tenantId);
        /// <summary>True if another page in this tenant already owns the slug (case-insensitive).</summary>
        Task<bool> SlugExists(Guid tenantId, string slug, Guid? excludePageId);
        Task<Guid> Create(TenantPage page);
        Task Update(TenantPage page);
        Task Delete(Guid id, Guid tenantId);
        /// <summary>Bulk-persist a drag-reorder of the tenant's pages.</summary>
        Task Reorder(Guid tenantId, IEnumerable<(Guid Id, int SortOrder)> order);
    }
}
