namespace Services.Storage
{
    public interface IImageStorage
    {
        Task<string> SaveAsync(Stream content, Guid tenantId, string kind, string fileExtension, CancellationToken ct = default);
        Task DeleteAsync(string publicUrl, CancellationToken ct = default);
    }
}
