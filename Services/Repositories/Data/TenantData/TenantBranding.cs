namespace Services.Repositories.Data.TenantData
{
    public class TenantBranding
    {
        public Guid TenantId { get; set; }
        public string PrimaryColor { get; set; } = "#1976D2";
        public string SecondaryColor { get; set; } = "#424242";
        public string AccentColor { get; set; } = "#82B1FF";
        public string? Tagline { get; set; }
        public string ThemeMode { get; set; } = "light";
        public string? LogoUrl { get; set; }
        public string? FaviconUrl { get; set; }
        public string? HeroImageUrl { get; set; }
        public string? SecondaryHeroUrl { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
