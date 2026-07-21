namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class TenantListItem
    {
        public Guid Id { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string Timezone { get; set; } = null!;
        public int ServiceChargeBps { get; set; }
        public int? MonthlyServiceChargeCapCents { get; set; }
        // 'platform' | 'direct' (charge on tenant's own connected account).
        public string StripeChargeMode { get; set; } = "platform";
        // Connect onboarding status (pending | active | restricted | null), so the super-admin UI can
        // warn that 'direct' mode needs an active connected account before it will take payments.
        public string? StripeConnectStatus { get; set; }
        public bool IsPublished { get; set; }
        // Tenant-level feature toggles (super-admin editable in the Feature Toggles tab).
        public bool GiftCardsEnabled { get; set; }
        public bool ExtrasEnabled { get; set; }
        public bool SeasonPassesEnabled { get; set; }
        public bool ConcessionsEnabled { get; set; }
        public bool BikeShopEnabled { get; set; }
        public bool BlogEnabled { get; set; }
        public bool MembershipEnabled { get; set; }
        public bool WaitlistEnabled { get; set; }
        public bool WaitlistPrepayEnabled { get; set; }
        public bool AllowSelfCancel { get; set; }
        public bool DynamicPricingEnabled { get; set; }
        public bool BundledCouponsEnabled { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? ContactEmail { get; set; }
        public string? Phone { get; set; }
        public string? LoampassMxDestinationId { get; set; }
        public string ClientType { get; set; } = "hosted";
        public string? CustomDomain { get; set; }
        public bool CustomDomainVerified { get; set; }
        public bool EmbedEnabled { get; set; }
        public string[]? EmbedAllowedOrigins { get; set; }
        public string? ExternalHomeUrl { get; set; }
        public string? ExternalEventsUrl { get; set; }
        public string EmbedEventTarget { get; set; } = "external";
        public DateTime CreatedAtUtc { get; set; }
        // Demo-seed state (stage/local only). SeedDataPopulated hides the button once used;
        // CanSeedData is the platform env flag (true only on stage/local), stamped per response.
        public bool SeedDataPopulated { get; set; }
        public bool CanSeedData { get; set; }
    }
}
