namespace Services.Repositories.Data.PlatformData
{
    /// <summary>
    /// Singleton landing-page content for the apex domain (ridepass.io).
    /// Mirrors the per-tenant branding pattern but at the platform level:
    /// super admins edit it, the public apex Home reads it, and no tenant
    /// owns it. Always exactly one row (id = 1, enforced by CHECK).
    /// </summary>
    public class PlatformBranding
    {
        public int Id { get; set; } = 1;

        // Nav-bar logo for the apex domain (parallels the per-tenant logo).
        public string? LogoUrl { get; set; }

        public string? HeroImageUrl { get; set; }
        public string? HeroHeadline { get; set; }
        public string? HeroSubhead { get; set; }
        public string? HeroCtaPrimaryLabel { get; set; }
        public string? HeroCtaPrimaryUrl { get; set; }
        public string? HeroCtaSecondaryLabel { get; set; }
        public string? HeroCtaSecondaryUrl { get; set; }

        public bool StatsShowTracks { get; set; } = true;
        public bool StatsShowEventDays { get; set; } = true;
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
        public string? NavBarHomeColor { get; set; }
        public string? NavBarHomeTextColor { get; set; }

        // For Tracks (operator-acquisition) page hero copy. The benefits block on
        // that page reuses BenefitsHtml / BenefitsImageUrl / SectionBenefitsTitle.
        public string? ForTracksHeroEyebrow { get; set; }
        public string? ForTracksHeroHeadline { get; set; }
        public string? ForTracksHeroSubhead { get; set; }

        public DateTime UpdatedAtUtc { get; set; }
    }
}
