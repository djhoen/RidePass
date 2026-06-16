using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.BlogData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Blog;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BlogController : ControllerBase
    {
        private readonly IBlogRepository _blog;
        private readonly ITenantContext _tenantContext;
        private readonly IImageStorage _imageStorage;

        public BlogController(IBlogRepository blog, ITenantContext tenantContext, IImageStorage imageStorage)
        {
            _blog = blog;
            _tenantContext = tenantContext;
            _imageStorage = imageStorage;
        }

        // ───────── Public (no auth; tenant resolved via subdomain; gated on blog_enabled) ─────────

        // Published posts for the resolved tenant, newest first. Literal routes ("Featured",
        // "Admin") take precedence over the "{slug}" catch below, so those slugs are reserved.
        [HttpGet]
        public async Task<IActionResult> ListPublished()
        {
            if (!RequirePublicBlog(out var error)) return error!;
            var posts = await _blog.ListForTenant(_tenantContext.TenantId, publishedOnly: true);
            var items = posts.Select(p => new PublicBlogListItem
            {
                Title = p.Title,
                Slug = p.Slug,
                Excerpt = p.Excerpt,
                MainImageUrl = p.MainImageUrl,
                PublishedAtUtc = AsUtc(p.PublishedAt),
            }).ToList();
            return new ApiResponses().OkResult(items);
        }

        [HttpGet("Featured")]
        public async Task<IActionResult> GetFeatured()
        {
            if (!RequirePublicBlog(out var error)) return error!;
            var post = await _blog.GetFeatured(_tenantContext.TenantId);
            // Null payload (data: null) is the "no featured post" signal the home page reads.
            if (post is null) return new ApiResponses().OkResult();
            return new ApiResponses().OkResult(await ToDetail(post));
        }

        [HttpGet("{slug}")]
        public async Task<IActionResult> GetBySlug(string slug)
        {
            if (!RequirePublicBlog(out var error)) return error!;
            var post = await _blog.GetBySlug(slug, _tenantContext.TenantId, publishedOnly: true);
            if (post is null) return new ApiResponses().NotFoundResult("Post not found.");
            return new ApiResponses().OkResult(await ToDetail(post));
        }

        // ───────── Admin (blog.manage) ─────────

        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpGet("Admin")]
        public async Task<IActionResult> ListAll()
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var posts = await _blog.ListForTenant(_tenantContext.TenantId, publishedOnly: false);
            var imagesByPost = await _blog.ListImagesForPosts(posts.Select(p => p.Id), _tenantContext.TenantId);
            var items = posts.Select(p => new BlogPostListItem
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                Status = p.Status,
                IsFeatured = p.IsFeatured,
                MainImageUrl = p.MainImageUrl,
                Excerpt = p.Excerpt,
                ImageCount = imagesByPost.TryGetValue(p.Id, out var imgs) ? imgs.Count : 0,
                PublishedAtUtc = AsUtc(p.PublishedAt),
                CreatedAtUtc = AsUtc(p.CreatedAt)!.Value,
                UpdatedAtUtc = AsUtc(p.UpdatedAt)!.Value,
            }).ToList();
            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpGet("Admin/{id:guid}")]
        public async Task<IActionResult> GetAdmin(Guid id)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var post = await _blog.GetById(id, _tenantContext.TenantId);
            if (post is null) return new ApiResponses().NotFoundResult("Post not found.");
            return new ApiResponses().OkResult(await ToDetail(post));
        }

        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertBlogPostRequest request)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var post = new BlogPost
            {
                TenantId = _tenantContext.TenantId,
                Title = request.Title.Trim(),
                Slug = await EnsureUniqueSlug(DeriveSlug(request.Slug, request.Title), null),
                Excerpt = Trim(request.Excerpt),
                BodyHtml = request.BodyHtml,
                MainImageUrl = Trim(request.MainImageUrl),
                Status = request.Status,
                PublishedAt = request.Status == "published" ? DateTime.UtcNow : null,
            };
            post.Id = await _blog.Create(post);
            return new ApiResponses().OkResult(await ToDetail(post));
        }

        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertBlogPostRequest request)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var existing = await _blog.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Post not found.");

            existing.Title = request.Title.Trim();
            existing.Slug = await EnsureUniqueSlug(DeriveSlug(request.Slug, request.Title), id);
            existing.Excerpt = Trim(request.Excerpt);
            existing.BodyHtml = request.BodyHtml;
            existing.MainImageUrl = Trim(request.MainImageUrl);

            // Stamp published_at the first time the post goes live. Flipping back to draft
            // also drops it out of the featured slot, so a hidden post can't hold the
            // home-page feature.
            existing.Status = request.Status;
            if (request.Status == "published" && existing.PublishedAt is null)
            {
                existing.PublishedAt = DateTime.UtcNow;
            }
            if (request.Status == "draft")
            {
                existing.IsFeatured = false;
            }

            await _blog.Update(existing);
            return new ApiResponses().OkResult(await ToDetail(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var existing = await _blog.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Post not found.");
            await _blog.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpPut("{id:guid}/Featured")]
        public async Task<IActionResult> SetFeatured(Guid id, [FromBody] SetBlogFeaturedRequest request)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var existing = await _blog.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Post not found.");
            if (request.Featured && existing.Status != "published")
            {
                return new ApiResponses().BadRequestResult("Only a published post can be featured on the home page.");
            }
            await _blog.SetFeatured(id, _tenantContext.TenantId, request.Featured);
            var updated = await _blog.GetById(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(await ToDetail(updated!));
        }

        // Main cover image upload, decoupled from row mutation (same pattern as
        // EventController): returns a URL the editor patches onto the post on save.
        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpPost("Image")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var (ext, error) = ValidateImage(file);
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "blog", ext!, ct);
            return new ApiResponses().OkResult(new { imageUrl = url });
        }

        // Add one of the "several other images" to a post and persist the row.
        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpPost("{id:guid}/Images")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> AddImage(Guid id, IFormFile file,
            [FromForm] string? caption, [FromForm] int sortOrder, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var post = await _blog.GetById(id, _tenantContext.TenantId);
            if (post is null) return new ApiResponses().NotFoundResult("Post not found.");
            var (ext, error) = ValidateImage(file);
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "blog", ext!, ct);
            var image = new BlogPostImage
            {
                BlogPostId = id,
                TenantId = _tenantContext.TenantId,
                ImageUrl = url,
                Caption = Trim(caption),
                SortOrder = sortOrder,
            };
            image.Id = await _blog.AddImage(image);
            return new ApiResponses().OkResult(ToImageDto(image));
        }

        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpPut("Images/{imageId:guid}")]
        public async Task<IActionResult> UpdateImage(Guid imageId, [FromBody] UpdateBlogImageRequest request)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var image = await _blog.GetImage(imageId, _tenantContext.TenantId);
            if (image is null) return new ApiResponses().NotFoundResult("Image not found.");
            var caption = Trim(request.Caption);
            await _blog.UpdateImageCaption(imageId, _tenantContext.TenantId, caption);
            image.Caption = caption;
            return new ApiResponses().OkResult(ToImageDto(image));
        }

        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpDelete("Images/{imageId:guid}")]
        public async Task<IActionResult> DeleteImage(Guid imageId)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var image = await _blog.GetImage(imageId, _tenantContext.TenantId);
            if (image is null) return new ApiResponses().NotFoundResult("Image not found.");
            await _blog.DeleteImage(imageId, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.BlogManage)]
        [HttpPost("{id:guid}/Images/Reorder")]
        public async Task<IActionResult> ReorderImages(Guid id, [FromBody] BlogReorderRequest request)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var post = await _blog.GetById(id, _tenantContext.TenantId);
            if (post is null) return new ApiResponses().NotFoundResult("Post not found.");
            await _blog.ReorderImages(id, _tenantContext.TenantId,
                request.Items.Select(i => (i.Id, i.SortOrder)));
            return new ApiResponses().OkResult();
        }

        // ───────── helpers ─────────

        private IActionResult NoTenant() =>
            new ApiResponses().BadRequestResult("No tenant resolved for this request.");

        // Public endpoints: tenant must resolve AND have the blog turned on. When off we
        // return 404 so the blog is invisible rather than signalling that it exists.
        private bool RequirePublicBlog(out IActionResult? error)
        {
            if (!_tenantContext.IsResolved)
            {
                error = NoTenant();
                return false;
            }
            if (!_tenantContext.Tenant.BlogEnabled)
            {
                error = new ApiResponses().NotFoundResult("Not found.");
                return false;
            }
            error = null;
            return true;
        }

        private async Task<BlogPostDetail> ToDetail(BlogPost p)
        {
            var images = await _blog.ListImages(p.Id, _tenantContext.TenantId);
            return new BlogPostDetail
            {
                Id = p.Id,
                Title = p.Title,
                Slug = p.Slug,
                Excerpt = p.Excerpt,
                BodyHtml = p.BodyHtml,
                MainImageUrl = p.MainImageUrl,
                Status = p.Status,
                IsFeatured = p.IsFeatured,
                PublishedAtUtc = AsUtc(p.PublishedAt),
                CreatedAtUtc = AsUtc(p.CreatedAt)!.Value,
                UpdatedAtUtc = AsUtc(p.UpdatedAt)!.Value,
                Images = images.Select(ToImageDto).ToList(),
            };
        }

        private static BlogPostImageDto ToImageDto(BlogPostImage i) => new()
        {
            Id = i.Id,
            ImageUrl = i.ImageUrl,
            Caption = i.Caption,
            SortOrder = i.SortOrder,
        };

        private static (string? ext, string? error) ValidateImage(IFormFile file)
        {
            if (file is null || file.Length == 0) return (null, "File is required.");
            if (file.Length > 5 * 1024 * 1024) return (null, "File exceeds 5 MB limit.");
            var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
            };
            if (!allowed.TryGetValue(file.ContentType, out var ext))
                return (null, $"Unsupported content type: {file.ContentType}.");
            return (ext, null);
        }

        // Resolve a unique-per-tenant slug, appending -2, -3, ... on collision.
        private async Task<string> EnsureUniqueSlug(string baseSlug, Guid? excludeId)
        {
            var slug = baseSlug;
            var n = 2;
            while (await _blog.SlugExists(_tenantContext.TenantId, slug, excludeId))
            {
                slug = $"{baseSlug}-{n++}";
            }
            return slug;
        }

        private static string DeriveSlug(string? requested, string title) =>
            Slugify(string.IsNullOrWhiteSpace(requested) ? title : requested);

        private static string Slugify(string input)
        {
            var lower = (input ?? "").Trim().ToLowerInvariant();
            var sb = new System.Text.StringBuilder(lower.Length);
            var lastHyphen = false;
            foreach (var ch in lower)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    lastHyphen = false;
                }
                else if (!lastHyphen && sb.Length > 0)
                {
                    sb.Append('-');
                    lastHyphen = true;
                }
            }
            var slug = sb.ToString().Trim('-');
            return slug.Length == 0 ? "post" : slug;
        }

        private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static DateTime? AsUtc(DateTime? dt) =>
            dt.HasValue ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc) : null;
    }
}
