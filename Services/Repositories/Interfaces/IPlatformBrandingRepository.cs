using Services.Repositories.Data.PlatformData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Platform-level landing-page content. Singleton settings + a curated
    /// testimonial list. No tenant scope by design — these are global to
    /// ridepass.io, owned by super admins.
    /// </summary>
    public interface IPlatformBrandingRepository
    {
        /// <summary>
        /// Read the singleton settings row. Returns null only if the seed
        /// migration somehow hasn't run; callers should treat that case as
        /// "render defaults" rather than as an error.
        /// </summary>
        Task<PlatformBranding?> Get();

        /// <summary>
        /// Replace the settings row with the provided values. Touches the
        /// editable fields only; the singleton id stays 1 and updated_at
        /// stamps to now.
        /// </summary>
        Task Upsert(PlatformBranding branding);

        /// <summary>
        /// Update a single image url column without touching the rest of the
        /// row. kind is one of "hero" / "benefits". Used by the image-upload
        /// endpoint so it can persist the new url without a full PUT.
        /// </summary>
        Task UpdateImageUrl(string kind, string? newUrl);

        /// <summary>
        /// Update only the For Tracks page fields (hero copy + benefits title/html),
        /// leaving the apex home-page columns untouched. Backs the dedicated For
        /// Tracks save endpoint so the two editors can't clobber each other.
        /// </summary>
        Task UpdateForTracks(PlatformBranding branding);

        // ── Testimonials ─────────────────────────────────────────────────────
        Task<List<PlatformTestimonial>> ListTestimonials(bool includeInactive = false);
        Task<PlatformTestimonial?> GetTestimonial(Guid id);
        Task<Guid> CreateTestimonial(PlatformTestimonial t);
        Task UpdateTestimonial(PlatformTestimonial t);
        Task DeleteTestimonial(Guid id);
        /// <summary>
        /// Bulk reorder. Driven by the admin drag-and-drop list. Updates
        /// sort_order on each id and leaves other columns alone.
        /// </summary>
        Task ReorderTestimonials(List<Guid> orderedIds);
    }
}
