using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Audit;
using Services.Helpers;
using Services.Repositories.Data.PlatformData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.PlatformBranding;

namespace webapi.Controllers
{
    /// <summary>
    /// Apex landing-page content. Public GET (anyone can read), super-admin
    /// PUT/POST/DELETE. No tenant context: this is platform-level content
    /// edited by the super admin and read by every visitor to ridepass.io.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PlatformBrandingController : ControllerBase
    {
        private static readonly HashSet<string> AllowedImageKinds = new(StringComparer.Ordinal)
        {
            "logo", "hero", "benefits", "testimonial"
        };

        private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/png"]     = ".png",
            ["image/jpeg"]    = ".jpg",
            ["image/webp"]    = ".webp",
        };

        private const long MaxUploadBytes = 5 * 1024 * 1024; // 5 MB

        private readonly IPlatformBrandingRepository _branding;
        private readonly IImageStorage _imageStorage;
        private readonly IAuditLogger _audit;

        public PlatformBrandingController(
            IPlatformBrandingRepository branding,
            IImageStorage imageStorage,
            IAuditLogger audit)
        {
            _branding = branding;
            _imageStorage = imageStorage;
            _audit = audit;
        }

        // ── Public read ──────────────────────────────────────────────────────

        /// <summary>
        /// Full landing-page payload for the apex Home. Anonymous on purpose;
        /// this is the marketing surface of ridepass.io and must work for
        /// signed-out visitors.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var branding = await _branding.Get();
            var testimonials = await _branding.ListTestimonials(includeInactive: false);

            return new ApiResponses().OkResult(ToResponse(branding, testimonials));
        }

        // ── Super admin writes ───────────────────────────────────────────────

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPut]
        public async Task<IActionResult> Save([FromBody] SavePlatformBrandingRequest req)
        {
            if (req is null) return new ApiResponses().BadRequestResult("Body required.");

            // Re-read existing so image urls are preserved (the save form
            // doesn't post them; they live on a separate upload endpoint).
            var existing = await _branding.Get();

            var merged = new PlatformBranding
            {
                Id = 1,
                LogoUrl = existing?.LogoUrl,
                HeroImageUrl = existing?.HeroImageUrl,
                BenefitsImageUrl = existing?.BenefitsImageUrl,

                HeroHeadline = req.HeroHeadline,
                HeroSubhead = req.HeroSubhead,
                HeroCtaPrimaryLabel = req.HeroCtaPrimaryLabel,
                HeroCtaPrimaryUrl = req.HeroCtaPrimaryUrl,
                HeroCtaSecondaryLabel = req.HeroCtaSecondaryLabel,
                HeroCtaSecondaryUrl = req.HeroCtaSecondaryUrl,
                StatsShowTracks = req.StatsShowTracks,
                StatsShowEventDays = req.StatsShowEventDays,
                StatsPriceLabel = req.StatsPriceLabel,
                SectionTracksTitle = req.SectionTracksTitle,
                SectionEventsTitle = req.SectionEventsTitle,
                // Benefits title/html + For Tracks hero now belong to the For Tracks page
                // (edited via its own endpoint); preserve them here since the home-page
                // save form no longer posts them.
                SectionBenefitsTitle = existing?.SectionBenefitsTitle,
                SectionTestimonialsTitle = req.SectionTestimonialsTitle,
                SectionTracksNearYouTitle = req.SectionTracksNearYouTitle,
                BenefitsHtml = existing?.BenefitsHtml,
                ForTracksHeroEyebrow = existing?.ForTracksHeroEyebrow,
                ForTracksHeroHeadline = existing?.ForTracksHeroHeadline,
                ForTracksHeroSubhead = existing?.ForTracksHeroSubhead,
                CtaBannerHeadline = req.CtaBannerHeadline,
                CtaBannerSubhead = req.CtaBannerSubhead,
                CtaBannerPriceLabel = req.CtaBannerPriceLabel,
                CtaBannerCtaLabel = req.CtaBannerCtaLabel,
                CtaBannerCtaUrl = req.CtaBannerCtaUrl,
                FeaturedTrackIds = req.FeaturedTrackIds,
                NavBarColor = req.NavBarColor,
                NavBarTextColor = req.NavBarTextColor,
                NavBarHomeColor = req.NavBarHomeColor,
                NavBarHomeTextColor = req.NavBarHomeTextColor,
            };

            await _branding.Upsert(merged);
            await _audit.Log("platform.branding.save", "Updated landing-page settings",
                targetKind: "platform_branding", targetId: Guid.Empty);

            return await Get();
        }

        /// <summary>
        /// Save the For Tracks (operator-acquisition) page content: hero copy plus
        /// the "Why Tracks love RidePass" benefits title/html. Narrow on purpose so it
        /// can't overwrite the apex home-page fields. The benefits image is uploaded
        /// through the shared "benefits" image endpoint.
        /// </summary>
        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPut("ForTracks")]
        public async Task<IActionResult> SaveForTracks([FromBody] SaveForTracksRequest req)
        {
            if (req is null) return new ApiResponses().BadRequestResult("Body required.");

            var existing = await _branding.Get() ?? new PlatformBranding { Id = 1 };
            existing.ForTracksHeroEyebrow = req.HeroEyebrow;
            existing.ForTracksHeroHeadline = req.HeroHeadline;
            existing.ForTracksHeroSubhead = req.HeroSubhead;
            existing.SectionBenefitsTitle = req.BenefitsTitle;
            existing.BenefitsHtml = req.BenefitsHtml;

            await _branding.UpdateForTracks(existing);
            await _audit.Log("platform.fortracks.save", "Updated For Tracks page content",
                targetKind: "platform_branding", targetId: Guid.Empty);

            return await Get();
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("Image/{kind}")]
        [RequestSizeLimit(MaxUploadBytes)]
        public async Task<IActionResult> UploadImage(string kind, IFormFile file, CancellationToken ct)
        {
            if (!AllowedImageKinds.Contains(kind))
                return new ApiResponses().BadRequestResult($"Invalid image kind: {kind}.");
            if (file is null || file.Length == 0)
                return new ApiResponses().BadRequestResult("File is required.");
            if (file.Length > MaxUploadBytes)
                return new ApiResponses().BadRequestResult("File exceeds 5 MB limit.");
            if (!AllowedContentTypes.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult($"Unsupported content type: {file.ContentType}.");

            // testimonial uploads return the URL only (caller wires it onto a
            // specific testimonial row). hero/benefits also persist the URL
            // onto the singleton branding row in one shot.
            await using var stream = file.OpenReadStream();
            var newUrl = await _imageStorage.SavePlatformAsync(stream, kind, ext, ct);

            if (kind == "logo" || kind == "hero" || kind == "benefits")
            {
                var existing = await _branding.Get();
                var oldUrl = kind switch
                {
                    "logo"     => existing?.LogoUrl,
                    "hero"     => existing?.HeroImageUrl,
                    _          => existing?.BenefitsImageUrl,
                };
                await _branding.UpdateImageUrl(kind, newUrl);
                if (!string.IsNullOrEmpty(oldUrl))
                {
                    await _imageStorage.DeleteAsync(oldUrl, ct);
                }
            }

            await _audit.Log("platform.branding.image.upload", $"Uploaded {kind} image",
                targetKind: "platform_branding", targetId: Guid.Empty, metadata: new { url = newUrl });

            return new ApiResponses().OkResult(new { url = newUrl });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpDelete("Image/{kind}")]
        public async Task<IActionResult> DeleteImage(string kind, CancellationToken ct)
        {
            if (kind != "logo" && kind != "hero" && kind != "benefits")
                return new ApiResponses().BadRequestResult($"Invalid image kind: {kind}.");

            var existing = await _branding.Get();
            var oldUrl = kind switch
            {
                "logo"     => existing?.LogoUrl,
                "hero"     => existing?.HeroImageUrl,
                _          => existing?.BenefitsImageUrl,
            };
            await _branding.UpdateImageUrl(kind, null);
            if (!string.IsNullOrEmpty(oldUrl))
            {
                await _imageStorage.DeleteAsync(oldUrl, ct);
            }
            await _audit.Log("platform.branding.image.delete", $"Removed {kind} image",
                targetKind: "platform_branding", targetId: Guid.Empty);
            return await Get();
        }

        // ── Testimonials CRUD ────────────────────────────────────────────────

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Testimonials")]
        public async Task<IActionResult> ListTestimonials([FromQuery] bool includeInactive = true)
        {
            var rows = await _branding.ListTestimonials(includeInactive);
            return new ApiResponses().OkResult(rows.Select(ToTestimonialResponse));
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("Testimonials")]
        public async Task<IActionResult> CreateTestimonial([FromBody] UpsertTestimonialRequest req)
        {
            // sort_order defaults are filled in by the DB; reorder is a
            // separate call when the admin drags rows around.
            var existing = await _branding.ListTestimonials(includeInactive: true);
            var nextSort = existing.Count == 0 ? 10 : existing.Max(t => t.SortOrder) + 10;

            var t = new PlatformTestimonial
            {
                SortOrder = nextSort,
                RiderName = req.RiderName.Trim(),
                Quote = req.Quote.Trim(),
                Rating = req.Rating,
                IsActive = req.IsActive,
            };
            t.Id = await _branding.CreateTestimonial(t);

            await _audit.Log("platform.testimonial.create",
                $"Added testimonial by {t.RiderName}",
                targetKind: "platform_testimonial", targetId: t.Id);

            return new ApiResponses().OkResult(ToTestimonialResponse(t));
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPut("Testimonials/{id:guid}")]
        public async Task<IActionResult> UpdateTestimonial(Guid id, [FromBody] UpsertTestimonialRequest req)
        {
            var existing = await _branding.GetTestimonial(id);
            if (existing is null) return new ApiResponses().NotFoundResult("Testimonial not found.");

            existing.RiderName = req.RiderName.Trim();
            existing.Quote = req.Quote.Trim();
            existing.Rating = req.Rating;
            existing.IsActive = req.IsActive;

            await _branding.UpdateTestimonial(existing);
            await _audit.Log("platform.testimonial.update",
                $"Updated testimonial {id}",
                targetKind: "platform_testimonial", targetId: id);
            return new ApiResponses().OkResult(ToTestimonialResponse(existing));
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpDelete("Testimonials/{id:guid}")]
        public async Task<IActionResult> DeleteTestimonial(Guid id)
        {
            var existing = await _branding.GetTestimonial(id);
            if (existing is null) return new ApiResponses().NotFoundResult("Testimonial not found.");
            await _branding.DeleteTestimonial(id);
            await _audit.Log("platform.testimonial.delete",
                $"Deleted testimonial by {existing.RiderName}",
                targetKind: "platform_testimonial", targetId: id);
            return new ApiResponses().OkResult(new { deleted = true });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("Testimonials/Reorder")]
        public async Task<IActionResult> ReorderTestimonials([FromBody] ReorderTestimonialsRequest req)
        {
            if (req?.OrderedIds is null || req.OrderedIds.Count == 0)
                return new ApiResponses().BadRequestResult("OrderedIds required.");
            await _branding.ReorderTestimonials(req.OrderedIds);
            return new ApiResponses().OkResult(new { ok = true });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("Testimonials/{id:guid}/Photo")]
        [RequestSizeLimit(MaxUploadBytes)]
        public async Task<IActionResult> UploadTestimonialPhoto(Guid id, IFormFile file, CancellationToken ct)
        {
            var existing = await _branding.GetTestimonial(id);
            if (existing is null) return new ApiResponses().NotFoundResult("Testimonial not found.");
            if (file is null || file.Length == 0)
                return new ApiResponses().BadRequestResult("File is required.");
            if (file.Length > MaxUploadBytes)
                return new ApiResponses().BadRequestResult("File exceeds 5 MB limit.");
            if (!AllowedContentTypes.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult($"Unsupported content type: {file.ContentType}.");

            await using var stream = file.OpenReadStream();
            var newUrl = await _imageStorage.SavePlatformAsync(stream, "testimonial", ext, ct);

            var oldUrl = existing.RiderPhotoUrl;
            existing.RiderPhotoUrl = newUrl;
            await _branding.UpdateTestimonial(existing);
            if (!string.IsNullOrEmpty(oldUrl))
            {
                await _imageStorage.DeleteAsync(oldUrl, ct);
            }

            return new ApiResponses().OkResult(ToTestimonialResponse(existing));
        }

        // ── Mapping helpers ──────────────────────────────────────────────────

        private static PlatformBrandingResponse ToResponse(PlatformBranding? b, List<PlatformTestimonial> testimonials)
        {
            // If the seed row is missing (shouldn't happen in practice), fall
            // back to empty defaults rather than 500 so the public page
            // degrades gracefully into "render the empty state".
            b ??= new PlatformBranding { Id = 1 };
            return new PlatformBrandingResponse
            {
                LogoUrl = b.LogoUrl,
                HeroImageUrl = b.HeroImageUrl,
                HeroHeadline = b.HeroHeadline,
                HeroSubhead = b.HeroSubhead,
                HeroCtaPrimaryLabel = b.HeroCtaPrimaryLabel,
                HeroCtaPrimaryUrl = b.HeroCtaPrimaryUrl,
                HeroCtaSecondaryLabel = b.HeroCtaSecondaryLabel,
                HeroCtaSecondaryUrl = b.HeroCtaSecondaryUrl,
                StatsShowTracks = b.StatsShowTracks,
                StatsShowEventDays = b.StatsShowEventDays,
                StatsPriceLabel = b.StatsPriceLabel,
                SectionTracksTitle = b.SectionTracksTitle,
                SectionEventsTitle = b.SectionEventsTitle,
                SectionBenefitsTitle = b.SectionBenefitsTitle,
                SectionTestimonialsTitle = b.SectionTestimonialsTitle,
                SectionTracksNearYouTitle = b.SectionTracksNearYouTitle,
                BenefitsHtml = b.BenefitsHtml,
                BenefitsImageUrl = b.BenefitsImageUrl,
                CtaBannerHeadline = b.CtaBannerHeadline,
                CtaBannerSubhead = b.CtaBannerSubhead,
                CtaBannerPriceLabel = b.CtaBannerPriceLabel,
                CtaBannerCtaLabel = b.CtaBannerCtaLabel,
                CtaBannerCtaUrl = b.CtaBannerCtaUrl,
                FeaturedTrackIds = b.FeaturedTrackIds,
                NavBarColor = b.NavBarColor,
                NavBarTextColor = b.NavBarTextColor,
                NavBarHomeColor = b.NavBarHomeColor,
                NavBarHomeTextColor = b.NavBarHomeTextColor,
                ForTracksHeroEyebrow = b.ForTracksHeroEyebrow,
                ForTracksHeroHeadline = b.ForTracksHeroHeadline,
                ForTracksHeroSubhead = b.ForTracksHeroSubhead,
                Testimonials = testimonials.Select(ToTestimonialResponse).ToList(),
            };
        }

        private static PlatformTestimonialResponse ToTestimonialResponse(PlatformTestimonial t) => new()
        {
            Id = t.Id,
            SortOrder = t.SortOrder,
            RiderName = t.RiderName,
            RiderPhotoUrl = t.RiderPhotoUrl,
            Quote = t.Quote,
            Rating = t.Rating,
            IsActive = t.IsActive,
        };
    }
}
