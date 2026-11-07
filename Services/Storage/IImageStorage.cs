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

        /// <summary>
        /// Save a PRIVATE tenant image (e.g. a rider's gate photo): never publicly readable, never
        /// under a static /uploads/ path. Returns an opaque storage key (not a URL); the bytes are
        /// served only through an authorized endpoint via <see cref="OpenPrivateAsync"/>.
        /// </summary>
        Task<string> SavePrivateAsync(Stream content, Guid tenantId, string kind, string fileExtension, CancellationToken ct = default);

        /// <summary>The bytes and content type of a private image, or null when it is gone.</summary>
        Task<(byte[] Bytes, string ContentType)?> OpenPrivateAsync(string key, CancellationToken ct = default);

        Task DeletePrivateAsync(string key, CancellationToken ct = default);

        /// <summary>Whether a private image still exists (a cheap existence check, no download).</summary>
        Task<bool> PrivateExistsAsync(string key, CancellationToken ct = default);
    }
}
