using Services.Helpers.Interfaces;
using Services.Repositories.Data.PlatformData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    /// <summary>
    /// Platform-level repository. PLATFORM SCOPE BY DESIGN: there is no
    /// tenant_id column to filter by, because these rows are global to
    /// ridepass.io. The standard /tenant-audit tenant-scope rule does not
    /// apply here; the writes are gated by SuperAdminRequirement at the
    /// controller layer instead.
    /// </summary>
    public class PlatformBrandingRepository : IPlatformBrandingRepository
    {
        private const string BrandingColumns = @"
            id,
            logo_url AS LogoUrl,
            hero_image_url AS HeroImageUrl,
            hero_headline AS HeroHeadline,
            hero_subhead AS HeroSubhead,
            hero_cta_primary_label AS HeroCtaPrimaryLabel,
            hero_cta_primary_url AS HeroCtaPrimaryUrl,
            hero_cta_secondary_label AS HeroCtaSecondaryLabel,
            hero_cta_secondary_url AS HeroCtaSecondaryUrl,
            stats_show_tracks AS StatsShowTracks,
            stats_show_event_days AS StatsShowEventDays,
            stats_price_label AS StatsPriceLabel,
            section_tracks_title AS SectionTracksTitle,
            section_events_title AS SectionEventsTitle,
            section_benefits_title AS SectionBenefitsTitle,
            section_testimonials_title AS SectionTestimonialsTitle,
            section_tracks_near_you_title AS SectionTracksNearYouTitle,
            benefits_html AS BenefitsHtml,
            benefits_image_url AS BenefitsImageUrl,
            cta_banner_headline AS CtaBannerHeadline,
            cta_banner_subhead AS CtaBannerSubhead,
            cta_banner_price_label AS CtaBannerPriceLabel,
            cta_banner_cta_label AS CtaBannerCtaLabel,
            cta_banner_cta_url AS CtaBannerCtaUrl,
            featured_track_ids AS FeaturedTrackIds,
            nav_bar_color AS NavBarColor,
            nav_bar_text_color AS NavBarTextColor,
            nav_bar_home_color AS NavBarHomeColor,
            nav_bar_home_text_color AS NavBarHomeTextColor,
            for_tracks_hero_eyebrow AS ForTracksHeroEyebrow,
            for_tracks_hero_headline AS ForTracksHeroHeadline,
            for_tracks_hero_subhead AS ForTracksHeroSubhead,
            updated_at_utc AS UpdatedAtUtc";

        private const string TestimonialColumns = @"
            id, sort_order AS SortOrder,
            rider_name AS RiderName, rider_photo_url AS RiderPhotoUrl,
            quote, rating, is_active AS IsActive,
            created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc";

        private readonly IDbHelper _db;

        public PlatformBrandingRepository(IDbHelper db) => _db = db;

        public async Task<PlatformBranding?> Get()
        {
            var sql = $"SELECT {BrandingColumns} FROM platform_branding WHERE id = 1 LIMIT 1";
            return (await _db.Query<PlatformBranding>(sql)).FirstOrDefault();
        }

        public async Task Upsert(PlatformBranding b)
        {
            // INSERT ... ON CONFLICT (id) DO UPDATE so the very first save
            // creates the row if the seed migration didn't (e.g. a manual
            // delete during dev), and subsequent saves overwrite cleanly.
            const string sql = @"
                INSERT INTO platform_branding (
                    id, logo_url, hero_image_url, hero_headline, hero_subhead,
                    hero_cta_primary_label, hero_cta_primary_url,
                    hero_cta_secondary_label, hero_cta_secondary_url,
                    stats_show_tracks, stats_show_event_days, stats_price_label,
                    section_tracks_title, section_events_title,
                    section_benefits_title, section_testimonials_title, section_tracks_near_you_title,
                    benefits_html, benefits_image_url,
                    cta_banner_headline, cta_banner_subhead, cta_banner_price_label,
                    cta_banner_cta_label, cta_banner_cta_url,
                    featured_track_ids,
                    nav_bar_color, nav_bar_text_color,
                    nav_bar_home_color, nav_bar_home_text_color,
                    for_tracks_hero_eyebrow, for_tracks_hero_headline, for_tracks_hero_subhead,
                    updated_at_utc
                ) VALUES (
                    1, @LogoUrl, @HeroImageUrl, @HeroHeadline, @HeroSubhead,
                    @HeroCtaPrimaryLabel, @HeroCtaPrimaryUrl,
                    @HeroCtaSecondaryLabel, @HeroCtaSecondaryUrl,
                    @StatsShowTracks, @StatsShowEventDays, @StatsPriceLabel,
                    @SectionTracksTitle, @SectionEventsTitle,
                    @SectionBenefitsTitle, @SectionTestimonialsTitle, @SectionTracksNearYouTitle,
                    @BenefitsHtml, @BenefitsImageUrl,
                    @CtaBannerHeadline, @CtaBannerSubhead, @CtaBannerPriceLabel,
                    @CtaBannerCtaLabel, @CtaBannerCtaUrl,
                    @FeaturedTrackIds,
                    @NavBarColor, @NavBarTextColor,
                    @NavBarHomeColor, @NavBarHomeTextColor,
                    @ForTracksHeroEyebrow, @ForTracksHeroHeadline, @ForTracksHeroSubhead,
                    now()
                )
                ON CONFLICT (id) DO UPDATE SET
                    logo_url = EXCLUDED.logo_url,
                    hero_image_url = EXCLUDED.hero_image_url,
                    hero_headline = EXCLUDED.hero_headline,
                    hero_subhead = EXCLUDED.hero_subhead,
                    hero_cta_primary_label = EXCLUDED.hero_cta_primary_label,
                    hero_cta_primary_url = EXCLUDED.hero_cta_primary_url,
                    hero_cta_secondary_label = EXCLUDED.hero_cta_secondary_label,
                    hero_cta_secondary_url = EXCLUDED.hero_cta_secondary_url,
                    stats_show_tracks = EXCLUDED.stats_show_tracks,
                    stats_show_event_days = EXCLUDED.stats_show_event_days,
                    stats_price_label = EXCLUDED.stats_price_label,
                    section_tracks_title = EXCLUDED.section_tracks_title,
                    section_events_title = EXCLUDED.section_events_title,
                    section_benefits_title = EXCLUDED.section_benefits_title,
                    section_testimonials_title = EXCLUDED.section_testimonials_title,
                    section_tracks_near_you_title = EXCLUDED.section_tracks_near_you_title,
                    benefits_html = EXCLUDED.benefits_html,
                    benefits_image_url = EXCLUDED.benefits_image_url,
                    cta_banner_headline = EXCLUDED.cta_banner_headline,
                    cta_banner_subhead = EXCLUDED.cta_banner_subhead,
                    cta_banner_price_label = EXCLUDED.cta_banner_price_label,
                    cta_banner_cta_label = EXCLUDED.cta_banner_cta_label,
                    cta_banner_cta_url = EXCLUDED.cta_banner_cta_url,
                    featured_track_ids = EXCLUDED.featured_track_ids,
                    nav_bar_color = EXCLUDED.nav_bar_color,
                    nav_bar_text_color = EXCLUDED.nav_bar_text_color,
                    nav_bar_home_color = EXCLUDED.nav_bar_home_color,
                    nav_bar_home_text_color = EXCLUDED.nav_bar_home_text_color,
                    for_tracks_hero_eyebrow = EXCLUDED.for_tracks_hero_eyebrow,
                    for_tracks_hero_headline = EXCLUDED.for_tracks_hero_headline,
                    for_tracks_hero_subhead = EXCLUDED.for_tracks_hero_subhead,
                    updated_at_utc = now()";
            await _db.Execute(sql, b);
        }

        /// <summary>
        /// Updates only the For Tracks page fields (hero copy + the "Why Tracks love
        /// RidePass" benefits title/html). Narrow on purpose so the For Tracks editor
        /// and the apex home editor never overwrite each other's columns. The benefits
        /// image is set via the separate "benefits" image upload endpoint.
        /// </summary>
        public async Task UpdateForTracks(PlatformBranding b)
        {
            const string sql = @"
                UPDATE platform_branding SET
                    for_tracks_hero_eyebrow  = @ForTracksHeroEyebrow,
                    for_tracks_hero_headline = @ForTracksHeroHeadline,
                    for_tracks_hero_subhead  = @ForTracksHeroSubhead,
                    section_benefits_title   = @SectionBenefitsTitle,
                    benefits_html            = @BenefitsHtml,
                    updated_at_utc = now()
                WHERE id = 1";
            await _db.Execute(sql, b);
        }

        public async Task UpdateImageUrl(string kind, string? newUrl)
        {
            // Two narrow image columns on the singleton row. Whitelist the
            // column name so kind can never escape into raw SQL.
            var column = kind switch
            {
                "logo"     => "logo_url",
                "hero"     => "hero_image_url",
                "benefits" => "benefits_image_url",
                _          => throw new ArgumentException($"Unknown image kind: {kind}", nameof(kind)),
            };
            var sql = $"UPDATE platform_branding SET {column} = @newUrl, updated_at_utc = now() WHERE id = 1";
            await _db.Execute(sql, new { newUrl });
        }

        // ── Testimonials ─────────────────────────────────────────────────────

        public async Task<List<PlatformTestimonial>> ListTestimonials(bool includeInactive = false)
        {
            var sql = $@"
                SELECT {TestimonialColumns}
                FROM platform_testimonial
                WHERE @includeInactive OR is_active = true
                ORDER BY sort_order, created_at_utc";
            return (await _db.Query<PlatformTestimonial>(sql, new { includeInactive })).ToList();
        }

        public async Task<PlatformTestimonial?> GetTestimonial(Guid id)
        {
            var sql = $"SELECT {TestimonialColumns} FROM platform_testimonial WHERE id = @id LIMIT 1";
            return (await _db.Query<PlatformTestimonial>(sql, new { id })).FirstOrDefault();
        }

        public async Task<Guid> CreateTestimonial(PlatformTestimonial t)
        {
            const string sql = @"
                INSERT INTO platform_testimonial
                    (sort_order, rider_name, rider_photo_url, quote, rating, is_active)
                VALUES
                    (@SortOrder, @RiderName, @RiderPhotoUrl, @Quote, @Rating, @IsActive)
                RETURNING id";
            return (await _db.Query<Guid>(sql, t)).First();
        }

        public async Task UpdateTestimonial(PlatformTestimonial t)
        {
            const string sql = @"
                UPDATE platform_testimonial
                SET sort_order = @SortOrder,
                    rider_name = @RiderName,
                    rider_photo_url = @RiderPhotoUrl,
                    quote = @Quote,
                    rating = @Rating,
                    is_active = @IsActive,
                    updated_at_utc = now()
                WHERE id = @Id";
            await _db.Execute(sql, t);
        }

        public async Task DeleteTestimonial(Guid id)
        {
            await _db.Execute("DELETE FROM platform_testimonial WHERE id = @id", new { id });
        }

        public async Task ReorderTestimonials(List<Guid> orderedIds)
        {
            // Single UPDATE keyed off the position of each id in the input
            // array, so the whole reorder happens in one round trip. Untouched
            // rows (not present in orderedIds) keep their current sort_order.
            if (orderedIds.Count == 0) return;
            const string sql = @"
                UPDATE platform_testimonial AS pt
                SET sort_order = ord.idx,
                    updated_at_utc = now()
                FROM unnest(@orderedIds::uuid[]) WITH ORDINALITY AS ord(id, idx)
                WHERE pt.id = ord.id";
            await _db.Execute(sql, new { orderedIds = orderedIds.ToArray() });
        }
    }
}
