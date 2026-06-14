namespace Services.Storage
{
    public interface IImageStorage
    {
        Task<string> SaveAsync(Stream content, Guid tenantId, string kind, string fileExtension, CancellationToken ct = default);

        /// <summary>
        /// Save a platform-scoped image (apex landing page hero, benefits
        /// photo, sponsor logos, etc.). Lives under /uploads/platform/ to
        /// stay distinct from any tenant's folder.
        /// </summary>
        Task<string> SavePlatformAsync(Stream content, string kind, string fileExtension, CancellationToken ct = default);

        Task DeleteAsync(string publicUrl, CancellationToken ct = default);
    }
}
