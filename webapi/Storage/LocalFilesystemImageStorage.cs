using Services.Storage;

namespace webapi.Storage
{
    public class LocalFilesystemImageStorage : IImageStorage
    {
        private readonly IWebHostEnvironment _env;

        private readonly string _privateRoot;

        public LocalFilesystemImageStorage(IWebHostEnvironment env, IConfiguration config)
        {
            _env = env;
            // Outside the deploy path and the web root (PrivateImageRoot); created at startup.
            _privateRoot = PrivateImageRoot.Resolve(config, env);
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

        // Private images live OUTSIDE wwwroot (UseStaticFiles never serves them) and outside the deploy
        // path, under PrivateImageRoot/{key}; the key's shape is checked first, so it can't escape it.
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

        public Task<bool> PrivateExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(PrivateImageKey.IsValid(key) && File.Exists(PrivatePath(key)));

        private string PrivatePath(string key) =>
            Path.Combine(_privateRoot, key.Replace('/', Path.DirectorySeparatorChar));

        private string GetWebRoot()
        {
            return string.IsNullOrEmpty(_env.WebRootPath)
                ? Path.Combine(_env.ContentRootPath, "wwwroot")
                : _env.WebRootPath;
        }
    }
}
