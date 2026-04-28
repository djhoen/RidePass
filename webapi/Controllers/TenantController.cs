using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Tenant;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TenantController : ControllerBase
    {
        private static readonly HashSet<string> AllowedImageKinds = new(StringComparer.Ordinal)
        {
            "logo", "favicon", "hero", "secondaryHero"
        };

        private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"]     = ".png",
            ["image/jpeg"]    = ".jpg",
            ["image/webp"]    = ".webp",
            ["image/svg+xml"] = ".svg",
            ["image/x-icon"]  = ".ico",
            ["image/vnd.microsoft.icon"] = ".ico",
        };

        private const long MaxUploadBytes = 5 * 1024 * 1024; // 5 MB

        private readonly ITenantBrandingRepository _branding;
        private readonly ITenantRepository _tenants;
        private readonly ITenantContext _tenantContext;
        private readonly IImageStorage _imageStorage;
        private readonly IConfiguration _configuration;

        public TenantController(
            ITenantBrandingRepository branding,
            ITenantRepository tenants,
            ITenantContext tenantContext,
            IImageStorage imageStorage,
            IConfiguration configuration)
        {
            _branding = branding;
            _tenants = tenants;
            _tenantContext = tenantContext;
            _imageStorage = imageStorage;
            _configuration = configuration;
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut]
        public async Task<IActionResult> UpdateTenantSettings([FromBody] UpdateTenantRequest request)
        {
            // Validate IANA timezone against the runtime's time-zone database.
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(request.Timezone);
            }
            catch (TimeZoneNotFoundException)
            {
                return new ApiResponses().BadRequestResult($"Unknown IANA timezone: {request.Timezone}.");
            }

            await _tenants.UpdateTimezone(_tenantContext.TenantId, request.Timezone);
            await _tenants.UpdateRequireReservation(_tenantContext.TenantId, request.RequireReservationForPasses);
            return await GetBranding();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Location")]
        public async Task<IActionResult> UpdateLocation([FromBody] UpdateTenantLocationRequest request)
        {
            if (request.Latitude is < -90 or > 90)
            {
                return new ApiResponses().BadRequestResult("Latitude must be between -90 and 90.");
            }
            if (request.Longitude is < -180 or > 180)
            {
                return new ApiResponses().BadRequestResult("Longitude must be between -180 and 180.");
            }
            var hasOneCoord = request.Latitude.HasValue != request.Longitude.HasValue;
            if (hasOneCoord)
            {
                return new ApiResponses().BadRequestResult("Latitude and longitude must both be provided or both empty.");
            }

            await _tenants.UpdateLocation(
                _tenantContext.TenantId,
                Trim(request.AddressLine),
                Trim(request.City),
                Trim(request.Region),
                Trim(request.PostalCode),
                Trim(request.Country),
                request.Latitude,
                request.Longitude);
            return await GetBranding();
        }

        private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        [HttpGet("Branding")]
        public async Task<IActionResult> GetBranding()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved for this request.");
            }

            var row = await _branding.GetByTenantId(_tenantContext.TenantId);
            if (row is null)
            {
                return new ApiResponses().NotFoundResult("Branding not found.");
            }

            // Re-read the tenant so settings written within this same request (timezone,
            // reservation toggle, location) are reflected back to the caller.
            var tenant = await _tenants.GetById(_tenantContext.TenantId) ?? _tenantContext.Tenant;

            var response = new GetBrandingResponse
            {
                TenantId = row.TenantId,
                Subdomain = tenant.Subdomain,
                DisplayName = tenant.DisplayName,
                Timezone = tenant.Timezone,
                PrimaryColor = row.PrimaryColor,
                SecondaryColor = row.SecondaryColor,
                AccentColor = row.AccentColor,
                Tagline = row.Tagline,
                ThemeMode = row.ThemeMode,
                LogoUrl = row.LogoUrl,
                FaviconUrl = row.FaviconUrl,
                HeroImageUrl = row.HeroImageUrl,
                SecondaryHeroUrl = row.SecondaryHeroUrl,
                StripePublishableKey = _configuration["Stripe:PublishableKey"],
                RequireReservationForPasses = tenant.RequireReservationForPasses,
                AddressLine = tenant.AddressLine,
                City = tenant.City,
                Region = tenant.Region,
                PostalCode = tenant.PostalCode,
                Country = tenant.Country,
                Latitude = tenant.Latitude,
                Longitude = tenant.Longitude,
            };

            return new ApiResponses().OkResult(response);
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Branding")]
        public async Task<IActionResult> UpdateBranding([FromBody] UpdateBrandingRequest request)
        {
            await _branding.UpdateMetadata(
                _tenantContext.TenantId,
                request.PrimaryColor,
                request.SecondaryColor,
                request.AccentColor,
                request.Tagline,
                request.ThemeMode);

            return await GetBranding();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("Branding/Image/{kind}")]
        [RequestSizeLimit(MaxUploadBytes)]
        public async Task<IActionResult> UploadBrandingImage(string kind, IFormFile file, CancellationToken ct)
        {
            if (!AllowedImageKinds.Contains(kind))
            {
                return new ApiResponses().BadRequestResult($"Invalid image kind: {kind}.");
            }

            if (file is null || file.Length == 0)
            {
                return new ApiResponses().BadRequestResult("File is required.");
            }

            if (file.Length > MaxUploadBytes)
            {
                return new ApiResponses().BadRequestResult("File exceeds 5 MB limit.");
            }

            if (!AllowedContentTypes.TryGetValue(file.ContentType, out var ext))
            {
                return new ApiResponses().BadRequestResult($"Unsupported content type: {file.ContentType}.");
            }

            var existing = await _branding.GetByTenantId(_tenantContext.TenantId);
            var oldUrl = existing is null ? null : kind switch
            {
                "logo"          => existing.LogoUrl,
                "favicon"       => existing.FaviconUrl,
                "hero"          => existing.HeroImageUrl,
                "secondaryHero" => existing.SecondaryHeroUrl,
                _               => null
            };

            await using var stream = file.OpenReadStream();
            var newUrl = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, kind, ext, ct);
            await _branding.UpdateImageUrl(_tenantContext.TenantId, kind, newUrl);

            if (!string.IsNullOrEmpty(oldUrl))
            {
                await _imageStorage.DeleteAsync(oldUrl, ct);
            }

            return await GetBranding();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpDelete("Branding/Image/{kind}")]
        public async Task<IActionResult> DeleteBrandingImage(string kind, CancellationToken ct)
        {
            if (!AllowedImageKinds.Contains(kind))
            {
                return new ApiResponses().BadRequestResult($"Invalid image kind: {kind}.");
            }

            var existing = await _branding.GetByTenantId(_tenantContext.TenantId);
            var oldUrl = existing is null ? null : kind switch
            {
                "logo"          => existing.LogoUrl,
                "favicon"       => existing.FaviconUrl,
                "hero"          => existing.HeroImageUrl,
                "secondaryHero" => existing.SecondaryHeroUrl,
                _               => null
            };

            await _branding.UpdateImageUrl(_tenantContext.TenantId, kind, null);
            if (!string.IsNullOrEmpty(oldUrl))
            {
                await _imageStorage.DeleteAsync(oldUrl, ct);
            }

            return await GetBranding();
        }
    }
}
