using Services.Storage;

namespace webapi.Storage
{
    public class LocalFilesystemImageStorage : IImageStorage
    {
        private readonly IWebHostEnvironment _env;

        public LocalFilesystemImageStorage(IWebHostEnvironment env)
        {
            _env = env;
        }

        public async Task<string> SaveAsync(Stream content, Guid tenantId, string kind, string fileExtension, CancellationToken ct = default)
        {
            var webRoot = GetWebRoot();
            var dir = Path.Combine(webRoot, "uploads", tenantId.ToString());
            Directory.CreateDirectory(dir);

            var fileName = $"{kind}-{Guid.NewGuid():N}{fileExtension}";
            var filePath = Path.Combine(dir, fileName);

            await using var file = File.Create(filePath);
            await content.CopyToAsync(file, ct);

            return $"/uploads/{tenantId}/{fileName}";
        }

        public async Task<string> SavePlatformAsync(Stream content, string kind, string fileExtension, CancellationToken ct = default)
        {
            // Mirrors SaveAsync but uses a fixed "platform" folder instead
            // of a per-tenant uuid. DeleteAsync still works on the returned
            // /uploads/platform/... url because it just strips the prefix.
            var webRoot = GetWebRoot();
            var dir = Path.Combine(webRoot, "uploads", "platform");
            Directory.CreateDirectory(dir);

            var fileName = $"{kind}-{Guid.NewGuid():N}{fileExtension}";
            var filePath = Path.Combine(dir, fileName);

            await using var file = File.Create(filePath);
            await content.CopyToAsync(file, ct);

            return $"/uploads/platform/{fileName}";
        }

        public Task DeleteAsync(string publicUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(publicUrl) || !publicUrl.StartsWith("/uploads/"))
            {
                return Task.CompletedTask;
            }

            var relative = publicUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var filePath = Path.Combine(GetWebRoot(), relative);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return Task.CompletedTask;
        }

        private string GetWebRoot()
        {
            return string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(_env.ContentRootPath, "wwwroot")
                : _env.WebRootPath;
        }
    }
}
