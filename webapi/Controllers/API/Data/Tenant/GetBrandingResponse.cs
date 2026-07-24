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
        public string? LogoWhiteUrl { get; set; }
        public string? FaviconUrl { get; set; }
        public string? HeroImageUrl { get; set; }
        public string? SecondaryHeroUrl { get; set; }
        public string? NavBarColor { get; set; }
        public string? NavBarTextColor { get; set; }
        public string? StripePublishableKey { get; set; }
        public bool RequireReservationForPasses { get; set; }
        public bool RequireEmergencyContact { get; set; }
        public bool AllowEventSubscriptions { get; set; }
        public bool RequireIdAtCheckin { get; set; }
        public string? StripeConnectAccountId { get; set; }
        public string? StripeConnectStatus { get; set; }
        // 'platform' (default) or 'direct'. When 'direct', the SPA must initialize Stripe.js with
        // { stripeAccount: StripeConnectAccountId } so the Payment Element confirms the direct charge
        // on the tenant's own connected account.
        public string StripeChargeMode { get; set; } = "platform";
        public int ServiceChargeBps { get; set; }
        /// <summary>Renter's share of the service charge on rentals (bps). Lets the booking screen
        /// show the same fee the server will charge.</summary>
        public int RentalRiderPaidServiceChargeBps { get; set; }
        /// <summary>Rental sales tax rate (bps). NULL = never configured; the admin UI warns.</summary>
        public int? RentalTaxBps { get; set; }
        public bool RentalTaxServiceChargeTaxable { get; set; } = true;
        public string? ShippingName { get; set; }
        public string? AboutHtml { get; set; }
        public string? HoursJson { get; set; }
        public string? HomeNextUpTitle { get; set; }
        public Guid[]? HomeNextUpEventTypeIds { get; set; }
        public string? HomeBenefitsHtml { get; set; }
        public string? HomeBenefitsImageUrl { get; set; }
        public string? HomeSectionsJson { get; set; }   // { sectionKey: bool }; missing key = visible
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
        public bool ExtrasEnabled { get; set; }
        public bool SeasonPassesEnabled { get; set; } = true;
        public bool ConcessionsEnabled { get; set; }
        public bool BikeShopEnabled { get; set; }
        /// <summary>Days after pickup to email a shop service reminder; 0 = off.</summary>
        public int ShopServiceReminderDays { get; set; }
        /// <summary>Email the customer when a repair is marked ready.</summary>
        public bool ShopReadyNotifyEmail { get; set; }
        /// <summary>Text the customer when a repair is marked ready (costs per send).</summary>
        public bool ShopReadyNotifySms { get; set; }
        /// <summary>Shop supply fee as bps of labor on a repair bill; 0 = off.</summary>
        public int ShopSupplyFeeBps { get; set; }
        public int? ShopSupplyFeeCapCents { get; set; }
        public string ShopSupplyFeeLabel { get; set; } = "Shop supplies";
        /// <summary>Default shop labor rate in cents/hour; null = no rate set.</summary>
        public int? ShopLaborRateCents { get; set; }
        public bool WristbandsEnabled { get; set; }
        public bool TracksideExportEnabled { get; set; }
        /// <summary>The tenant has ever configured a spectator ticket tier; drives the
        /// Spectator Report nav visibility.</summary>
        public bool SellsSpectatorPasses { get; set; }
        public bool BlogEnabled { get; set; }
        // Stepped price ladders / bundled share-coupons (super-admin toggles). The tenant
        // admin UI hides the corresponding tier-editor sections when these are off.
        public bool DynamicPricingEnabled { get; set; }
        public bool BundledCouponsEnabled { get; set; }
        // True when this tenant is a LoamPassMx track (a destination id is configured).
        // The destination id itself stays server-side.
        public bool LoampassMxEnabled { get; set; }
        // Embedded-widget config: whether embedding is on, and the origins allowed to
        // frame the widgets. Read by the chromeless /embed routes to guard rendering.
        public bool EmbedEnabled { get; set; }
        public string[]? EmbedAllowedOrigins { get; set; }
        // First-party origins allowed to embed any tenant (global allow-list). The client
        // guard checks the parent against (tenant ∪ global); the authoritative control is
        // the server-stamped frame-ancestors CSP.
        public string[]? GlobalEmbedAllowedOrigins { get; set; }
        // Front-door config so the SPA can redirect the subdomain to the tenant's
        // real home (custom domain or an embedded client's external site).
        public string ClientType { get; set; } = "hosted";
        public string? CustomDomain { get; set; }
        public bool CustomDomainVerified { get; set; }
        public string? ExternalHomeUrl { get; set; }
        public string? ExternalEventsUrl { get; set; }
        // Embedded-client apex event-click target: 'external' (their site) or 'ridepass'
        // (the hosted event page). Read by the SPA front-door redirect to NOT bounce
        // /Event/:id when set to 'ridepass'.
        public string EmbedEventTarget { get; set; } = "external";
        public bool AllowSelfCancel { get; set; }
        // Custom gate-fee section headings shown at checkout / event pricing;
        // null = platform defaults ("Riding Pass" / "Spectator Pass").
        public string? RiderGateLabel { get; set; }
        public string? SpectatorGateLabel { get; set; }
        public bool WaitlistEnabled { get; set; } = false;
        public bool WaitlistPrepayEnabled { get; set; } = false;
        public int WaitlistConfirmWindowMinutes { get; set; }
        public bool MembershipEnabled { get; set; }
        public string MembershipName { get; set; } = "Track Membership";
        public int MembershipPriceCents { get; set; }
        public string MembershipDurationKind { get; set; } = "yearly";
        public bool MembershipRequiredForRiders { get; set; } = true;
        public bool MembershipRequiredForSpectators { get; set; }
        // Published, nav-visible custom pages for this tenant, in sort order. Rendered as
        // top-level links (public top bar + drawer) alongside the built-in Blog link.
        public List<NavPageItem> NavPages { get; set; } = new();
    }

    /// <summary>One entry in GetBrandingResponse.NavPages: a public custom-page nav link.</summary>
    public class NavPageItem
    {
        public string Slug { get; set; } = null!;
        public string Label { get; set; } = null!;
    }
}
