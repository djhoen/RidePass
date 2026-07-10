using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.PageData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Page;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PageController : ControllerBase
    {
        // Slugs that would collide with a real top-level route if a tenant page were
        // allowed to claim them. Checked case-insensitively; kept in sync with the SPA's
        // static top-level routes (see router.ts) since the public "{slug}" page route is
        // registered last, right before the 404 catch-all.
        private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
        {
            "", "events", "event", "blog", "giftcard", "rentals", "order", "login", "logout",
            "resetpassword", "verifyemail", "redeem", "admin", "superadmin", "user", "embed",
            "p", "fortracks", "survey", "cart", "checkout", "api", "uploads", "assets",
        };

        private readonly IPageRepository _pages;
        private readonly ITenantContext _tenantContext;
        private readonly IImageStorage _imageStorage;

        public PageController(IPageRepository pages, ITenantContext tenantContext, IImageStorage imageStorage)
        {
            _pages = pages;
            _tenantContext = tenantContext;
            _imageStorage = imageStorage;
        }

        // ───────── Public (no auth; tenant resolved via subdomain; published only) ─────────

        // Root-level clean URL: {subdomain}.ridepass.io/{slug}. "Admin" is a reserved slug
        // (see ReservedSlugs) so it never collides with the routes below.
        [HttpGet("{slug}")]
        public async Task<IActionResult> GetBySlug(string slug)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var page = await _pages.GetBySlug(slug, _tenantContext.TenantId, publishedOnly: true);
            if (page is null) return new ApiResponses().NotFoundResult("Page not found.");
            return new ApiResponses().OkResult(ToPublic(page));
        }

        // ───────── Admin (settings.manage) ─────────

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpGet("Admin")]
        public async Task<IActionResult> ListAll()
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var pages = await _pages.ListAll(_tenantContext.TenantId, publishedOnly: false);
            var items = pages.Select(ToListItem).ToList();
            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpGet("Admin/{id:guid}")]
        public async Task<IActionResult> GetAdmin(Guid id)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var page = await _pages.GetById(id, _tenantContext.TenantId);
            if (page is null) return new ApiResponses().NotFoundResult("Page not found.");
            return new ApiResponses().OkResult(ToDetail(page));
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertPageRequest request)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var slug = DeriveSlug(request.Slug, request.Title);
            if (!IsSlugAllowed(slug, out var slugError)) return new ApiResponses().BadRequestResult(slugError!);

            var page = new TenantPage
            {
                TenantId = _tenantContext.TenantId,
                Title = request.Title.Trim(),
                Slug = await EnsureUniqueSlug(slug, null),
                BodyHtml = request.BodyHtml,
                HeroImageUrl = Trim(request.HeroImageUrl),
                Status = request.Status,
                ShowInNav = request.ShowInNav,
                NavLabel = Trim(request.NavLabel),
                SortOrder = request.SortOrder,
                PublishedAt = request.Status == "published" ? DateTime.UtcNow : null,
            };
            page.Id = await _pages.Create(page);
            return new ApiResponses().OkResult(ToDetail(page));
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertPageRequest request)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var existing = await _pages.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Page not found.");

            var slug = DeriveSlug(request.Slug, request.Title);
            if (!IsSlugAllowed(slug, out var slugError)) return new ApiResponses().BadRequestResult(slugError!);

            existing.Title = request.Title.Trim();
            existing.Slug = await EnsureUniqueSlug(slug, id);
            existing.BodyHtml = request.BodyHtml;
            existing.HeroImageUrl = Trim(request.HeroImageUrl);
            existing.ShowInNav = request.ShowInNav;
            existing.NavLabel = Trim(request.NavLabel);
            existing.SortOrder = request.SortOrder;

            // Stamp published_at the first time the page goes live.
            existing.Status = request.Status;
            if (request.Status == "published" && existing.PublishedAt is null)
            {
                existing.PublishedAt = DateTime.UtcNow;
            }

            await _pages.Update(existing);
            return new ApiResponses().OkResult(ToDetail(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var existing = await _pages.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Page not found.");
            await _pages.Delete(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // Hero / inline-body image upload, decoupled from row mutation (same pattern as
        // BlogController): returns a URL the editor patches onto the page (or inserts
        // inline into the rich-text body) on save.
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("Image")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            var (ext, error) = ValidateImage(file);
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "pages", ext!, ct);
            return new ApiResponses().OkResult(new { imageUrl = url });
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Reorder")]
        public async Task<IActionResult> Reorder([FromBody] PageReorderRequest request)
        {
            if (!_tenantContext.IsResolved) return NoTenant();
            await _pages.Reorder(_tenantContext.TenantId,
                request.Items.Select(i => (i.Id, i.SortOrder)));
            return new ApiResponses().OkResult();
        }

        // ───────── helpers ─────────

        private IActionResult NoTenant() =>
            new ApiResponses().BadRequestResult("No tenant resolved for this request.");

        private static PageListItem ToListItem(TenantPage p) => new()
        {
            Id = p.Id,
            Title = p.Title,
            Slug = p.Slug,
            Status = p.Status,
            ShowInNav = p.ShowInNav,
            NavLabel = p.NavLabel,
            SortOrder = p.SortOrder,
            HeroImageUrl = p.HeroImageUrl,
            PublishedAtUtc = AsUtc(p.PublishedAt),
            CreatedAtUtc = AsUtc(p.CreatedAt)!.Value,
            UpdatedAtUtc = AsUtc(p.UpdatedAt)!.Value,
        };

        private static PageDetail ToDetail(TenantPage p) => new()
        {
            Id = p.Id,
            Title = p.Title,
            Slug = p.Slug,
            BodyHtml = p.BodyHtml,
            HeroImageUrl = p.HeroImageUrl,
            Status = p.Status,
            ShowInNav = p.ShowInNav,
            NavLabel = p.NavLabel,
            SortOrder = p.SortOrder,
            PublishedAtUtc = AsUtc(p.PublishedAt),
            CreatedAtUtc = AsUtc(p.CreatedAt)!.Value,
            UpdatedAtUtc = AsUtc(p.UpdatedAt)!.Value,
        };

        private static PublicPageResponse ToPublic(TenantPage p) => new()
        {
            Title = p.Title,
            Slug = p.Slug,
            BodyHtml = p.BodyHtml,
            HeroImageUrl = p.HeroImageUrl,
            PublishedAtUtc = AsUtc(p.PublishedAt),
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
            while (await _pages.SlugExists(_tenantContext.TenantId, slug, excludeId))
            {
                slug = $"{baseSlug}-{n++}";
            }
            return slug;
        }

        private static string DeriveSlug(string? requested, string title) =>
            Slugify(string.IsNullOrWhiteSpace(requested) ? title : requested);

        // Rejects a slug that would collide with a real top-level route, or that contains
        // '/' or whitespace (Slugify already strips both, but guard explicitly since a
        // reserved-slug check must not be bypassable by a request that skips derivation).
        private static bool IsSlugAllowed(string slug, out string? error)
        {
            if (ReservedSlugs.Contains(slug))
            {
                error = $"\"{slug}\" is a reserved page name and can't be used as a URL.";
                return false;
            }
            if (slug.Contains('/') || slug.Any(char.IsWhiteSpace))
            {
                error = "Slug can't contain slashes or whitespace.";
                return false;
            }
            error = null;
            return true;
        }

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
            return slug.Length == 0 ? "page" : slug;
        }

        private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static DateTime? AsUtc(DateTime? dt) =>
            dt.HasValue ? DateTime.SpecifyKind(dt.Value, DateTimeKind.Utc) : null;
    }
}
