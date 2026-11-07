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

        // Private images live OUTSIDE wwwroot (UseStaticFiles never serves them), under
        // {ContentRoot}/App_Data/private-images/{key}; the key's shape is checked first, so it can't
        // escape that folder.
        public async Task<string> SavePrivateAsync(Stream content, Guid tenantId, string kind, string fileExtension, CancellationToken ct = default)
        {
            var key = PrivateImageKey.New(tenantId, kind, fileExtension);
            var path = PrivatePath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var file = File.Create(path);
            await content.CopyToAsync(file, ct);
            return key;
        }

        public async Task<(byte[] Bytes, string ContentType)?> OpenPrivateAsync(string key, CancellationToken ct = default)
        {
            if (!PrivateImageKey.IsValid(key)) return null;
            var path = PrivatePath(key);
            if (!File.Exists(path)) return null;
            return (await File.ReadAllBytesAsync(path, ct), PrivateImageKey.ContentType(key));
        }

        public Task DeletePrivateAsync(string key, CancellationToken ct = default)
        {
            if (!PrivateImageKey.IsValid(key)) return Task.CompletedTask;
            var path = PrivatePath(key);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        private string PrivatePath(string key) =>
            Path.Combine(_env.ContentRootPath, "App_Data", "private-images",
                key.Replace('/', Path.DirectorySeparatorChar));

        private string GetWebRoot()
        {
            return string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(_env.ContentRootPath, "wwwroot")
                : _env.WebRootPath;
        }
    }
}
