using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
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
        private readonly IPaymentProvider _payments;
        private readonly IHomePageRepository _homePage;
        private readonly IConfiguration _configuration;

        public TenantController(
            ITenantBrandingRepository branding,
            ITenantRepository tenants,
            ITenantContext tenantContext,
            IImageStorage imageStorage,
            IPaymentProvider payments,
            IHomePageRepository homePage,
            IConfiguration configuration)
        {
            _branding = branding;
            _tenants = tenants;
            _tenantContext = tenantContext;
            _imageStorage = imageStorage;
            _payments = payments;
            _homePage = homePage;
            _configuration = configuration;
        }

        // ── Stripe Connect onboarding ───────────────────────────────────────────
        /// <summary>
        /// Tenant admin clicks "Connect Stripe" → we either create a new Standard account
        /// (first time) or reuse the existing one, then return a Stripe-hosted onboarding
        /// URL for them to complete KYC. Stripe redirects them back to the settings page.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("StripeConnect/Onboard")]
        public async Task<IActionResult> StartStripeConnectOnboarding(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenant = _tenantContext.Tenant;

            string accountId;
            if (string.IsNullOrEmpty(tenant.StripeConnectAccountId))
            {
                accountId = await _payments.CreateConnectAccountAsync(
                    tenantEmail: $"connect+{tenant.Subdomain}@ridepass.io",   // Stripe needs an email; we use a tenant-scoped one
                    tenantDisplayName: tenant.DisplayName,
                    ct: ct);
                await _tenants.SetStripeConnectAccount(tenant.Id, accountId, "pending");
            }
            else
            {
                accountId = tenant.StripeConnectAccountId;
            }

            var apex = _configuration["App:RootDomain"] ?? "ridepass.io";
            var baseUrl = $"https://{tenant.Subdomain}.{apex}/Admin/Settings/Payments";
            var url = await _payments.CreateAccountLinkAsync(
                accountId,
                returnUrl: $"{baseUrl}?stripe=connect_complete",
                refreshUrl: $"{baseUrl}?stripe=connect_refresh",
                ct: ct);

            return new ApiResponses().OkResult(new { onboardingUrl = url, accountId });
        }

        /// <summary>
        /// Re-poll Stripe for the connected account's status. The webhook also keeps this
        /// in sync, but on first onboarding-complete redirect the webhook may lag, so the
        /// frontend hits this to refresh.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("StripeConnect/Refresh")]
        public async Task<IActionResult> RefreshStripeConnectStatus(CancellationToken ct)
        {
            var tenant = _tenantContext.Tenant;
            if (string.IsNullOrEmpty(tenant.StripeConnectAccountId))
            {
                return new ApiResponses().BadRequestResult("No connected account on file.");
            }
            var status = await _payments.GetConnectAccountStatusAsync(tenant.StripeConnectAccountId, ct);
            await _tenants.UpdateStripeConnectStatus(tenant.StripeConnectAccountId, status);
            return new ApiResponses().OkResult(new { status });
        }

        /// <summary>
        /// Disconnect: clears the Stripe Connect link on our side. Note: this does NOT
        /// delete the tenant's Stripe account — they keep it and can disconnect from the
        /// platform via their Stripe Dashboard if they want a clean break.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpDelete("StripeConnect")]
        public async Task<IActionResult> DisconnectStripe()
        {
            await _tenants.ClearStripeConnect(_tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        /// <summary>
        /// Round-trips a no-op call to Stripe acting on the connected account, to confirm
        /// the integration is wired up correctly. Catches and surfaces Stripe errors so the
        /// admin gets a useful "what's wrong" message instead of a 500.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("StripeConnect/Test")]
        public async Task<IActionResult> TestStripeConnect(CancellationToken ct)
        {
            var tenant = _tenantContext.Tenant;
            if (string.IsNullOrEmpty(tenant.StripeConnectAccountId))
            {
                return new ApiResponses().BadRequestResult("No connected account on file.");
            }
            try
            {
                var result = await _payments.TestConnectAccountAsync(tenant.StripeConnectAccountId, ct);
                return new ApiResponses().OkResult(result);
            }
            catch (Stripe.StripeException ex)
            {
                return new ApiResponses().BadRequestResult($"Stripe rejected the call: {ex.StripeError?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult($"Test failed: {ex.Message}");
            }
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
            await _tenants.UpdateRequireEmergencyContact(_tenantContext.TenantId, request.RequireEmergencyContact);
            await _tenants.UpdateAllowEventSubscriptions(_tenantContext.TenantId, request.AllowEventSubscriptions);
            return await GetBranding();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("GiftCardSettings")]
        public async Task<IActionResult> UpdateGiftCardSettings([FromBody] UpdateGiftCardSettingsRequest request)
        {
            if (request.MinCents < 100) return new ApiResponses().BadRequestResult("Minimum must be at least $1.");
            if (request.MaxCents < request.MinCents) return new ApiResponses().BadRequestResult("Maximum must be ≥ minimum.");
            if (request.MaxCents > 1_000_000) return new ApiResponses().BadRequestResult("Maximum can't exceed $10,000.");
            await _tenants.UpdateGiftCardSettings(_tenantContext.TenantId, request.Enabled, request.MinCents, request.MaxCents);
            return await GetBranding();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("RentalsEnabled")]
        public async Task<IActionResult> UpdateRentalsEnabled([FromBody] webapi.Controllers.API.Data.Rental.UpdateRentalsEnabledRequest request)
        {
            await _tenants.UpdateRentalsEnabled(_tenantContext.TenantId, request.Enabled);
            return await GetBranding();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("ExtrasEnabled")]
        public async Task<IActionResult> UpdateExtrasEnabled([FromBody] webapi.Controllers.API.Data.Extras.UpdateExtrasEnabledRequest request)
        {
            await _tenants.UpdateExtrasEnabled(_tenantContext.TenantId, request.Enabled);
            return await GetBranding();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("SeasonPassesEnabled")]
        public async Task<IActionResult> UpdateSeasonPassesEnabled([FromBody] UpdateSeasonPassesEnabledRequest request)
        {
            await _tenants.UpdateSeasonPassesEnabled(_tenantContext.TenantId, request.Enabled);
            return await GetBranding();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("CancellationPolicy")]
        public async Task<IActionResult> UpdateCancellationPolicy([FromBody] UpdateCancellationPolicyRequest request)
        {
            await _tenants.UpdateCancellationPolicy(_tenantContext.TenantId, request.AllowSelfCancel,
                request.WaitlistEnabled, request.WaitlistConfirmWindowMinutes);
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
                Trim(request.ShippingName),
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

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Home/Content")]
        public async Task<IActionResult> UpdateHomeContent([FromBody] UpdateTenantHomeContentRequest request)
        {
            // Empty whitelist arrays should be persisted as NULL so "show all" is the
            // unambiguous default state rather than "show none".
            var typeIds = request.HomeNextUpEventTypeIds is { Length: > 0 }
                ? request.HomeNextUpEventTypeIds : null;
            await _tenants.UpdateHomeContent(_tenantContext.TenantId,
                aboutHtml: Trim(request.AboutHtml),
                hoursJson: request.HoursJson,
                homeNextUpTitle: Trim(request.HomeNextUpTitle),
                homeNextUpEventTypeIds: typeIds);
            return await GetBranding();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Home/DailyStatus")]
        public async Task<IActionResult> UpdateDailyStatus([FromBody] UpdateTenantDailyStatusRequest request)
        {
            await _tenants.UpdateDailyStatus(_tenantContext.TenantId, request.Open, Trim(request.Message));
            return await GetBranding();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Home/Footer")]
        public async Task<IActionResult> UpdateFooter([FromBody] UpdateTenantFooterRequest request)
        {
            await _tenants.UpdateFooter(_tenantContext.TenantId,
                contactEmail: Trim(request.ContactEmail),
                phone: Trim(request.Phone),
                facebook: Trim(request.SocialFacebookUrl),
                instagram: Trim(request.SocialInstagramUrl),
                tiktok: Trim(request.SocialTiktokUrl),
                youtube: Trim(request.SocialYoutubeUrl),
                refundPolicyHtml: Trim(request.RefundPolicyHtml));
            return await GetBranding();
        }

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
                TenantType = tenant.TenantType,
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
                RequireEmergencyContact = tenant.RequireEmergencyContact,
                AllowEventSubscriptions = tenant.AllowEventSubscriptions,
                StripeConnectAccountId = tenant.StripeConnectAccountId,
                StripeConnectStatus = tenant.StripeConnectStatus,
                ServiceChargeBps = tenant.ServiceChargeBps,
                ShippingName = tenant.ShippingName,
                AboutHtml = tenant.AboutHtml,
                HoursJson = tenant.HoursJson,
                HomeNextUpTitle = tenant.HomeNextUpTitle,
                HomeNextUpEventTypeIds = tenant.HomeNextUpEventTypeIds,
                DailyStatusOpen = tenant.DailyStatusOpen,
                DailyStatusMessage = tenant.DailyStatusMessage,
                DailyStatusUpdatedAt = tenant.DailyStatusUpdatedAt,
                ContactEmail = tenant.ContactEmail,
                SocialFacebookUrl = tenant.SocialFacebookUrl,
                SocialInstagramUrl = tenant.SocialInstagramUrl,
                SocialTiktokUrl = tenant.SocialTiktokUrl,
                SocialYoutubeUrl = tenant.SocialYoutubeUrl,
                RefundPolicyHtml = tenant.RefundPolicyHtml,
                AddressLine = tenant.AddressLine,
                City = tenant.City,
                Region = tenant.Region,
                PostalCode = tenant.PostalCode,
                Country = tenant.Country,
                Latitude = tenant.Latitude,
                Longitude = tenant.Longitude,
                GiftCardsEnabled = tenant.GiftCardsEnabled,
                GiftCardMinCents = tenant.GiftCardMinCents,
                GiftCardMaxCents = tenant.GiftCardMaxCents,
                Phone = tenant.Phone,
                RentalsEnabled = tenant.RentalsEnabled,
                ExtrasEnabled = tenant.ExtrasEnabled,
                SeasonPassesEnabled = tenant.SeasonPassesEnabled,
                AllowSelfCancel = tenant.AllowSelfCancel,
                WaitlistEnabled = tenant.WaitlistEnabled,
                WaitlistConfirmWindowMinutes = tenant.WaitlistConfirmWindowMinutes,
                MembershipEnabled = tenant.MembershipEnabled,
                MembershipName = tenant.MembershipName,
                MembershipPriceCents = tenant.MembershipPriceCents,
                MembershipDurationKind = tenant.MembershipDurationKind,
                MembershipRequiredForRiders = tenant.MembershipRequiredForRiders,
                MembershipRequiredForSpectators = tenant.MembershipRequiredForSpectators,
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

        // ── Gallery (multiple per tenant) ────────────────────────────────────────

        [HttpGet("Home/Gallery")]
        public async Task<IActionResult> ListGallery()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _homePage.ListGallery(_tenantContext.TenantId);
            return new ApiResponses().OkResult(rows);
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("Home/Gallery")]
        [RequestSizeLimit(MaxUploadBytes)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AddGalleryImage(IFormFile file, [FromForm] string? caption, [FromForm] int sortOrder, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                return new ApiResponses().BadRequestResult("File is required.");
            if (file.Length > MaxUploadBytes)
                return new ApiResponses().BadRequestResult("File exceeds 5 MB limit.");
            if (!AllowedContentTypes.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult($"Unsupported content type: {file.ContentType}.");

            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "gallery", ext, ct);
            var id = await _homePage.AddGalleryImage(_tenantContext.TenantId, url, Trim(caption), sortOrder);
            return new ApiResponses().OkResult(new { id, imageUrl = url });
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Home/Gallery/{id:guid}")]
        public async Task<IActionResult> UpdateGalleryImage(Guid id, [FromBody] UpdateGalleryImageRequest request)
        {
            await _homePage.UpdateGalleryImage(id, _tenantContext.TenantId, Trim(request.Caption), request.SortOrder);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpDelete("Home/Gallery/{id:guid}")]
        public async Task<IActionResult> DeleteGalleryImage(Guid id, CancellationToken ct)
        {
            // Look up the row first so we can clean up the underlying image file.
            var existing = (await _homePage.ListGallery(_tenantContext.TenantId)).FirstOrDefault(g => g.Id == id);
            await _homePage.DeleteGalleryImage(id, _tenantContext.TenantId);
            if (existing is not null) await _imageStorage.DeleteAsync(existing.ImageUrl, ct);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("Home/Gallery/Reorder")]
        public async Task<IActionResult> ReorderGallery([FromBody] ReorderGalleryRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            var ids = req.Items.Select(i => i.Id).ToList();
            var orders = req.Items.Select(i => i.SortOrder).ToList();
            await _homePage.UpdateGallerySortOrders(_tenantContext.TenantId, ids, orders);
            return new ApiResponses().OkResult();
        }

        // ── Track graphics ───────────────────────────────────────────────────────

        [HttpGet("Home/TrackGraphics")]
        public async Task<IActionResult> ListTrackGraphics()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _homePage.ListTrackGraphics(_tenantContext.TenantId);
            return new ApiResponses().OkResult(rows);
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("Home/TrackGraphics")]
        [RequestSizeLimit(MaxUploadBytes)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> AddTrackGraphic(IFormFile file, [FromForm] string? title,
            [FromForm] string? description, [FromForm] int sortOrder, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                return new ApiResponses().BadRequestResult("File is required.");
            if (file.Length > MaxUploadBytes)
                return new ApiResponses().BadRequestResult("File exceeds 5 MB limit.");
            if (!AllowedContentTypes.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult($"Unsupported content type: {file.ContentType}.");

            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "track", ext, ct);
            var id = await _homePage.AddTrackGraphic(_tenantContext.TenantId, url, Trim(title), Trim(description), sortOrder);
            return new ApiResponses().OkResult(new { id, imageUrl = url });
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Home/TrackGraphics/{id:guid}")]
        public async Task<IActionResult> UpdateTrackGraphic(Guid id, [FromBody] UpdateTrackGraphicRequest request)
        {
            await _homePage.UpdateTrackGraphic(id, _tenantContext.TenantId,
                Trim(request.Title), Trim(request.Description), request.SortOrder);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpDelete("Home/TrackGraphics/{id:guid}")]
        public async Task<IActionResult> DeleteTrackGraphic(Guid id, CancellationToken ct)
        {
            var existing = (await _homePage.ListTrackGraphics(_tenantContext.TenantId)).FirstOrDefault(g => g.Id == id);
            await _homePage.DeleteTrackGraphic(id, _tenantContext.TenantId);
            if (existing is not null) await _imageStorage.DeleteAsync(existing.ImageUrl, ct);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("Home/TrackGraphics/Reorder")]
        public async Task<IActionResult> ReorderTrackGraphics([FromBody] ReorderTrackGraphicsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            var ids = req.Items.Select(i => i.Id).ToList();
            var orders = req.Items.Select(i => i.SortOrder).ToList();
            await _homePage.UpdateTrackGraphicSortOrders(_tenantContext.TenantId, ids, orders);
            return new ApiResponses().OkResult();
        }
    }
}
