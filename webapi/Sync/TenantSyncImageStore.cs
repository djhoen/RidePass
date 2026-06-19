using Amazon.S3;
using Amazon.S3.Model;

namespace webapi.Sync
{
    public class SyncImage
    {
        // Object key relative to the bucket root / web root, e.g. uploads/{tenantId}/logo-x.png
        public string Key { get; set; } = null!;
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = "application/octet-stream";
    }

    /// <summary>
    /// Reads/writes a tenant's image objects for stage->prod promotion. Uses DO Spaces when
    /// Storage:Spaces:Bucket is configured (the real environments), else the local
    /// wwwroot/uploads disk (dev). Each environment is wired only to its OWN bucket, so the
    /// export (on stage) reads the stage bucket and the import (on prod) writes the prod
    /// bucket — no cross-account access. PublicBaseUrl is this env's image URL prefix, used
    /// by the import to rewrite stored URLs from the stage base to the prod base.
    /// </summary>
    public class TenantSyncImageStore
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly bool _useSpaces;
        private readonly string? _bucket;

        public string PublicBaseUrl { get; }

        public TenantSyncImageStore(IConfiguration config, IWebHostEnvironment env)
        {
            _config = config;
            _env = env;
            _bucket = config["Storage:Spaces:Bucket"];
            _useSpaces = !string.IsNullOrEmpty(_bucket);
            if (_useSpaces)
            {
                var serviceUrl = (config["Storage:Spaces:ServiceUrl"] ?? string.Empty).TrimEnd('/');
                PublicBaseUrl = (config["Storage:Spaces:PublicBaseUrl"] ?? $"{serviceUrl}/{_bucket}").TrimEnd('/');
            }
            else
            {
                PublicBaseUrl = string.Empty; // disk: relative /uploads/ URLs, identical across envs
            }
        }

        public async Task<List<SyncImage>> ReadTenantImages(Guid tenantId, CancellationToken ct = default)
        {
            var prefix = $"uploads/{tenantId}/";
            var images = new List<SyncImage>();

            if (_useSpaces)
            {
                using var s3 = CreateS3();
                string? token = null;
                do
                {
                    var resp = await s3.ListObjectsV2Async(new ListObjectsV2Request
                    {
                        BucketName = _bucket,
                        Prefix = prefix,
                        ContinuationToken = token,
                    }, ct);
                    foreach (var o in resp.S3Objects)
                    {
                        using var obj = await s3.GetObjectAsync(_bucket, o.Key, ct);
                        using var ms = new MemoryStream();
                        await obj.ResponseStream.CopyToAsync(ms, ct);
                        images.Add(new SyncImage
                        {
                            Key = o.Key,
                            Bytes = ms.ToArray(),
                            ContentType = obj.Headers.ContentType ?? ContentTypeFor(Path.GetExtension(o.Key)),
                        });
                    }
                    token = resp.IsTruncated == true ? resp.NextContinuationToken : null;
                } while (token != null);
            }
            else
            {
                var dir = Path.Combine(WebRoot(), "uploads", tenantId.ToString());
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        var key = Path.GetRelativePath(WebRoot(), f).Replace('\\', '/');
                        images.Add(new SyncImage
                        {
                            Key = key,
                            Bytes = await File.ReadAllBytesAsync(f, ct),
                            ContentType = ContentTypeFor(Path.GetExtension(f)),
                        });
                    }
                }
            }
            return images;
        }

        public async Task PutObject(SyncImage img, CancellationToken ct = default)
        {
            if (_useSpaces)
            {
                using var s3 = CreateS3();
                using var ms = new MemoryStream(img.Bytes);
                await s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _bucket,
                    Key = img.Key,
                    InputStream = ms,
                    ContentType = img.ContentType,
                    CannedACL = S3CannedACL.PublicRead,
                }, ct);
            }
            else
            {
                var path = Path.Combine(WebRoot(), img.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, img.Bytes, ct);
            }
        }

        private IAmazonS3 CreateS3() => new AmazonS3Client(
            _config["Storage:Spaces:AccessKey"],
            _config["Storage:Spaces:SecretKey"],
            new AmazonS3Config { ServiceURL = _config["Storage:Spaces:ServiceUrl"] });

        private string WebRoot() => string.IsNullOrEmpty(_env.WebRootPath)
            ? Path.Combine(_env.ContentRootPath, "wwwroot")
            : _env.WebRootPath;

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
