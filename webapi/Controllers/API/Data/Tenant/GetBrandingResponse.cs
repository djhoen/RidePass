namespace webapi.Controllers.API.Data.Tenant
{
    public class GetBrandingResponse
    {
        public Guid TenantId { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string Timezone { get; set; } = null!;
        public string PrimaryColor { get; set; } = null!;
        public string SecondaryColor { get; set; } = null!;
        public string AccentColor { get; set; } = null!;
        public string? Tagline { get; set; }
        public string ThemeMode { get; set; } = null!;
        public string? LogoUrl { get; set; }
        public string? FaviconUrl { get; set; }
        public string? HeroImageUrl { get; set; }
        public string? SecondaryHeroUrl { get; set; }
        public string? StripePublishableKey { get; set; }
        public bool RequireReservationForPasses { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
