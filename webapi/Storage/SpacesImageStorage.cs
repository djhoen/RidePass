using Amazon.S3;
using Amazon.S3.Model;
using Services.Storage;

namespace webapi.Storage
{
    /// <summary>
    /// Stores uploaded images in a DigitalOcean Spaces (S3-compatible) bucket and returns
    /// ABSOLUTE public URLs. Selected over LocalFilesystemImageStorage when
    /// Storage:Spaces:Bucket is configured.
    ///
    /// Why absolute URLs matter: a staging DB cloned from production then renders prod's
    /// images straight from prod's public bucket, with no file copy, because the stored
    /// URL is the bucket URL rather than a host-relative /uploads/ path.
    /// </summary>
    public class SpacesImageStorage : IImageStorage
    {
        private readonly IAmazonS3 _s3;
        private readonly string _bucket;
        private readonly string _publicBaseUrl;   // no trailing slash

        public SpacesImageStorage(IConfiguration config)
        {
            var serviceUrl = Require(config, "Storage:Spaces:ServiceUrl"); // e.g. https://sfo3.digitaloceanspaces.com
            var accessKey = Require(config, "Storage:Spaces:AccessKey");
            var secretKey = Require(config, "Storage:Spaces:SecretKey");
            _bucket = Require(config, "Storage:Spaces:Bucket");

            // Public base for the returned URLs. Set this to the CDN endpoint for caching;
            // otherwise default to the path-style origin (works for public-read objects).
            _publicBaseUrl = (config["Storage:Spaces:PublicBaseUrl"] ?? $"{serviceUrl.TrimEnd('/')}/{_bucket}")
                .TrimEnd('/');

            _s3 = new AmazonS3Client(accessKey, secretKey, new AmazonS3Config
            {
                ServiceURL = serviceUrl,   // region is embedded here; no AWS RegionEndpoint needed
            });
        }

        public Task<string> SaveAsync(Stream content, Guid tenantId, string kind, string fileExtension, CancellationToken ct = default)
            => PutAsync($"uploads/{tenantId}/{kind}-{Guid.NewGuid():N}{fileExtension}", content, fileExtension, ct);

        public Task<string> SavePlatformAsync(Stream content, string kind, string fileExtension, CancellationToken ct = default)
            => PutAsync($"uploads/platform/{kind}-{Guid.NewGuid():N}{fileExtension}", content, fileExtension, ct);

        private async Task<string> PutAsync(string key, Stream content, string fileExtension, CancellationToken ct)
        {
            // Buffer to a seekable stream with a known length so the SDK sends a normal
            // signed PUT. DO Spaces rejects the streaming/chunked signature the SDK uses
            // for non-seekable streams. Uploads are capped small (<=5MB) so this is cheap.
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            ms.Position = 0;

            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = ms,
                ContentType = ContentTypeFor(fileExtension),
                CannedACL = S3CannedACL.PublicRead,
            }, ct);

            return $"{_publicBaseUrl}/{key}";
        }

        public async Task DeleteAsync(string publicUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(publicUrl)) return;
            var prefix = _publicBaseUrl + "/";
            // Only delete objects we own; ignore legacy /uploads/ relative paths or other hosts.
            if (!publicUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
            var key = publicUrl.Substring(prefix.Length);
            if (key.Length == 0) return;
            await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = key }, ct);
        }

        private static string Require(IConfiguration config, string key) =>
            config[key] ?? throw new InvalidOperationException($"{key} is not configured.");

        private static string ContentTypeFor(string ext) => ext.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".gif" => "image/gif",
            _ => "application/octet-stream",
        };
    }
}
