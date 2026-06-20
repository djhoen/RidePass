namespace webapi.Controllers.API.Data.PlatformBranding
{
    /// <summary>
    /// Full landing-page content payload returned by GET /api/PlatformBranding.
    /// Read by the apex Home (anonymous) and by the SuperAdmin landing-page
    /// editor (super admin).
    /// </summary>
    public class PlatformBrandingResponse
    {
        public string? LogoUrl { get; set; }
        public string? HeroImageUrl { get; set; }
        public string? HeroHeadline { get; set; }
        public string? HeroSubhead { get; set; }
        public string? HeroCtaPrimaryLabel { get; set; }
        public string? HeroCtaPrimaryUrl { get; set; }
        public string? HeroCtaSecondaryLabel { get; set; }
        public string? HeroCtaSecondaryUrl { get; set; }

        public bool StatsShowTracks { get; set; }
        public bool StatsShowEventDays { get; set; }
        public string? StatsPriceLabel { get; set; }

        public string? SectionTracksTitle { get; set; }
        public string? SectionEventsTitle { get; set; }
        public string? SectionBenefitsTitle { get; set; }
        public string? SectionTestimonialsTitle { get; set; }
        public string? SectionTracksNearYouTitle { get; set; }

        public string? BenefitsHtml { get; set; }
        public string? BenefitsImageUrl { get; set; }

        public string? CtaBannerHeadline { get; set; }
        public string? CtaBannerSubhead { get; set; }
        public string? CtaBannerPriceLabel { get; set; }
        public string? CtaBannerCtaLabel { get; set; }
        public string? CtaBannerCtaUrl { get; set; }

        public Guid[]? FeaturedTrackIds { get; set; }

        public string? NavBarColor { get; set; }
        public string? NavBarTextColor { get; set; }

        public string? ForTracksHeroEyebrow { get; set; }
        public string? ForTracksHeroHeadline { get; set; }
        public string? ForTracksHeroSubhead { get; set; }

        public List<PlatformTestimonialResponse> Testimonials { get; set; } = new();
    }
}
