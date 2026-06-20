using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.PlatformBranding
{
    /// <summary>
    /// Settings-only save (excludes images and testimonials, which have their
    /// own endpoints). All fields optional so an admin can clear copy by
    /// passing null. MaxLength caps protect against runaway pastes.
    /// </summary>
    public class SavePlatformBrandingRequest
    {
        [MaxLength(200)] public string? HeroHeadline { get; set; }
        [MaxLength(500)] public string? HeroSubhead { get; set; }
        [MaxLength(80)]  public string? HeroCtaPrimaryLabel { get; set; }
        [MaxLength(300)] public string? HeroCtaPrimaryUrl { get; set; }
        [MaxLength(80)]  public string? HeroCtaSecondaryLabel { get; set; }
        [MaxLength(300)] public string? HeroCtaSecondaryUrl { get; set; }

        public bool StatsShowTracks { get; set; } = true;
        public bool StatsShowEventDays { get; set; } = true;
        [MaxLength(50)] public string? StatsPriceLabel { get; set; }

        [MaxLength(120)] public string? SectionTracksTitle { get; set; }
        [MaxLength(120)] public string? SectionEventsTitle { get; set; }
        [MaxLength(120)] public string? SectionBenefitsTitle { get; set; }
        [MaxLength(120)] public string? SectionTestimonialsTitle { get; set; }
        [MaxLength(120)] public string? SectionTracksNearYouTitle { get; set; }

        // HTML block bigger than the strings, but still want a sane cap to
        // catch runaway pastes. 50KB is plenty for marketing copy.
        [MaxLength(50000)] public string? BenefitsHtml { get; set; }

        [MaxLength(200)] public string? CtaBannerHeadline { get; set; }
        [MaxLength(500)] public string? CtaBannerSubhead { get; set; }
        [MaxLength(50)]  public string? CtaBannerPriceLabel { get; set; }
        [MaxLength(80)]  public string? CtaBannerCtaLabel { get; set; }
        [MaxLength(300)] public string? CtaBannerCtaUrl { get; set; }

        public Guid[]? FeaturedTrackIds { get; set; }

        // Nav bar color. NULL falls back to theme primary at render time.
        // The home-page override is nullable so leaving it blank inherits
        // the rest-of-site color.
        [RegularExpression("^#[0-9A-Fa-f]{6}$")]
        public string? NavBarColor { get; set; }

        [RegularExpression("^#[0-9A-Fa-f]{6}$")]
        public string? NavBarTextColor { get; set; }
    }
}
