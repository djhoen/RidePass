namespace webapi.Controllers.API.Data.Tenant
{
    public class GetBrandingResponse
    {
        public Guid TenantId { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string TenantType { get; set; } = null!;     // 'motocross' | 'mountain_bike'
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
        public string? NavBarColor { get; set; }
        public string? NavBarTextColor { get; set; }
        public string? NavBarHomeColor { get; set; }
        public string? NavBarHomeTextColor { get; set; }
        public string? StripePublishableKey { get; set; }
        public bool RequireReservationForPasses { get; set; }
        public bool RequireEmergencyContact { get; set; }
        public bool AllowEventSubscriptions { get; set; }
        public string? StripeConnectAccountId { get; set; }
        public string? StripeConnectStatus { get; set; }
        public int ServiceChargeBps { get; set; }
        public string? ShippingName { get; set; }
        public string? AboutHtml { get; set; }
        public string? HoursJson { get; set; }
        public string? HomeNextUpTitle { get; set; }
        public Guid[]? HomeNextUpEventTypeIds { get; set; }
        public bool? DailyStatusOpen { get; set; }
        public string? DailyStatusMessage { get; set; }
        public DateTime? DailyStatusUpdatedAt { get; set; }
        public string? ContactEmail { get; set; }
        public string? SocialFacebookUrl { get; set; }
        public string? SocialInstagramUrl { get; set; }
        public string? SocialTiktokUrl { get; set; }
        public string? SocialYoutubeUrl { get; set; }
        public string? RefundPolicyHtml { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public bool GiftCardsEnabled { get; set; }
        public int GiftCardMinCents { get; set; }
        public int GiftCardMaxCents { get; set; }
        public string? Phone { get; set; }
        public bool RentalsEnabled { get; set; }
        public bool ExtrasEnabled { get; set; }
        public bool SeasonPassesEnabled { get; set; } = true;
        public bool ConcessionsEnabled { get; set; }
        public bool AllowSelfCancel { get; set; }
        public bool WaitlistEnabled { get; set; } = true;
        public int WaitlistConfirmWindowMinutes { get; set; }
        public bool MembershipEnabled { get; set; }
        public string MembershipName { get; set; } = "Track Membership";
        public int MembershipPriceCents { get; set; }
        public string MembershipDurationKind { get; set; } = "yearly";
        public bool MembershipRequiredForRiders { get; set; } = true;
        public bool MembershipRequiredForSpectators { get; set; }
    }
}
