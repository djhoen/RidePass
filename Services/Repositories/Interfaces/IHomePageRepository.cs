using Services.Repositories.Data.TenantData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Read/write the per-tenant gallery photos and track-layout graphics that appear
    /// on the public home page. Both share the same shape (image + caption/description
    /// + sort order) but are stored separately so the admin can curate them
    /// independently and the public page can lay them out differently.
    /// </summary>
    public interface IHomePageRepository
    {
        Task<List<TenantGalleryImage>> ListGallery(Guid tenantId);
        Task<Guid> AddGalleryImage(Guid tenantId, string imageUrl, string? caption, int sortOrder);
        Task UpdateGalleryImage(Guid id, Guid tenantId, string? caption, int sortOrder);
        Task DeleteGalleryImage(Guid id, Guid tenantId);
        /// <summary>Atomic bulk update of sort_order for many gallery images at once.</summary>
        Task UpdateGallerySortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);

        Task<List<TenantTrackGraphic>> ListTrackGraphics(Guid tenantId);
        Task<Guid> AddTrackGraphic(Guid tenantId, string imageUrl, string? title, string? description, int sortOrder);
        Task UpdateTrackGraphic(Guid id, Guid tenantId, string? title, string? description, int sortOrder);
        Task DeleteTrackGraphic(Guid id, Guid tenantId);
        /// <summary>Atomic bulk update of sort_order for many track graphics at once.</summary>
        Task UpdateTrackGraphicSortOrders(Guid tenantId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders);
    }
}
