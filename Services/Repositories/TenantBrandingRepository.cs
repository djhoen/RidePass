using Services.Helpers.Interfaces;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantBrandingRepository : ITenantBrandingRepository
    {
        private static readonly Dictionary<string, string> ImageKindToColumn = new()
        {
            ["logo"]          = "logo_url",
            ["logoWhite"]     = "logo_white_url",
            ["favicon"]       = "favicon_url",
            ["hero"]          = "hero_image_url",
            ["secondaryHero"] = "secondary_hero_url",
            ["benefits"]      = "home_benefits_image_url",
        };

        private readonly IDbHelper _db;

        public TenantBrandingRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<TenantBranding?> GetByTenantId(Guid tenantId)
        {
            const string sql = @"
                SELECT tenant_id            AS TenantId,
                       primary_color        AS PrimaryColor,
                       secondary_color      AS SecondaryColor,
                       accent_color         AS AccentColor,
                       tagline,
                       theme_mode           AS ThemeMode,
                       logo_url             AS LogoUrl,
                       logo_white_url       AS LogoWhiteUrl,
                       favicon_url          AS FaviconUrl,
                       hero_image_url       AS HeroImageUrl,
                       secondary_hero_url   AS SecondaryHeroUrl,
                       home_benefits_image_url AS HomeBenefitsImageUrl,
                       nav_bar_color           AS NavBarColor,
                       nav_bar_text_color      AS NavBarTextColor,
                       updated_at              AS UpdatedAt
                FROM tenant_branding
                WHERE tenant_id = @tenantId
                LIMIT 1";

            var result = await _db.Query<TenantBranding>(sql, new { tenantId });
            return result.FirstOrDefault();
        }

        public async Task UpdateMetadata(Guid tenantId, string primaryColor, string secondaryColor, string accentColor,
                                         string? tagline, string themeMode,
                                         string? navBarColor, string? navBarTextColor)
        {
            const string sql = @"
                UPDATE tenant_branding
                   SET primary_color           = @primaryColor,
                       secondary_color         = @secondaryColor,
                       accent_color            = @accentColor,
                       tagline                 = @tagline,
                       theme_mode              = @themeMode,
                       nav_bar_color           = @navBarColor,
                       nav_bar_text_color      = @navBarTextColor
                 WHERE tenant_id = @tenantId";

            await _db.Execute(sql, new
            {
                tenantId, primaryColor, secondaryColor, accentColor, tagline, themeMode,
                navBarColor, navBarTextColor,
            });
        }

        public async Task UpdateImageUrl(Guid tenantId, string kind, string? url)
        {
            if (!ImageKindToColumn.TryGetValue(kind, out var column))
            {
                throw new ArgumentException($"Unknown image kind: {kind}", nameof(kind));
            }

            var sql = $"UPDATE tenant_branding SET {column} = @url WHERE tenant_id = @tenantId";
            await _db.Execute(sql, new { tenantId, url });
        }
    }
}
