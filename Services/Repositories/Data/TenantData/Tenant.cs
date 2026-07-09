namespace Services.Repositories.Data.TenantData
{
    public class Tenant
    {
        public Guid Id { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string Status { get; set; } = null!;
        // 'motocross' | 'mountain_bike'. Drives provisioning defaults at creation
        // time (event types, waiver wording). Changed at the database via
        // migration only — there's no admin UI to flip an existing tenant's type
        // because doing so wouldn't retroactively rewrite their event types.
        public string TenantType { get; set; } = "motocross";
        // MTB sub-classification driving provisioning defaults + discovery display:
        // 'bike_park' | 'shuttle' | 'resort'. NULL for MX and legacy tenants.
        public string? VenueCategory { get; set; }
        public string Timezone { get; set; } = "UTC";
        public bool RequireReservationForPasses { get; set; }
        public bool RequireEmergencyContact { get; set; }
        public bool AllowEventSubscriptions { get; set; }
        // When true, gate staff must attest they verified the rider's photo ID against
        // the purchaser name before redeeming. Enforced server-side in RedemptionController.
        public bool RequireIdAtCheckin { get; set; }
        public string? StripeConnectAccountId { get; set; }
        public string? StripeConnectStatus { get; set; }      // pending | active | restricted
        // 'platform' (default): charges run on the platform account, internal split, monthly payout.
        // 'direct': charges run on the tenant's own connected account with an application fee = our
        // service fee; the tenant is merchant of record and there is no platform payout. Required for
        // tenants exceeding the $1M/yr card-network sub-merchant threshold. Super-admin controlled.
        public string StripeChargeMode { get; set; } = "platform";
        // Lazily provisioned the first time a cashier opens the mobile app at
        // this tenant — required for Stripe Terminal tap-to-pay.
        public string? StripeTerminalLocationId { get; set; }
        // Terminal Location on the tenant's OWN connected account, provisioned lazily for 'direct'
        // mode card-present sales. Separate from StripeTerminalLocationId (the platform-account one).
        public string? StripeConnectedTerminalLocationId { get; set; }
        // Per-tenant Twilio Subaccount, provisioned lazily via Settings → SMS.
        // AuthToken is stored encrypted via EncryptionHelper; consumers must
        // decrypt before passing to Twilio. Until populated, SMS sends fall
        // back to the global Sms:Twilio:* config (transition-only fallback).
        public string? TwilioSubaccountSid { get; set; }
        public string? TwilioAuthTokenEncrypted { get; set; }
        public string? TwilioFromNumber { get; set; }       // E.164, e.g. +18885551234
        // Per-tenant Messaging Service that owns the sender pool (currently
        // a single toll-free number; short codes / 10DLC long codes attach
        // here later). When set, TwilioSmsSender routes through this instead
        // of binding directly to TwilioFromNumber.
        public string? TwilioMessagingServiceSid { get; set; }
        public bool SmsEnabled { get; set; }
        public DateTime? SmsEnabledAtUtc { get; set; }
        public int ServiceChargeBps { get; set; }
        public int? MonthlyServiceChargeCapCents { get; set; }
        public string? ShippingName { get; set; }
        public string? AboutHtml { get; set; }
        public string? HoursJson { get; set; }
        public string? HomeNextUpTitle { get; set; }
        public Guid[]? HomeNextUpEventTypeIds { get; set; }
        public string? HomeBenefitsHtml { get; set; }
        public string? HomeSectionsJson { get; set; }   // { sectionKey: bool }; missing key = visible
        public bool? DailyStatusOpen { get; set; }
        public string? DailyStatusMessage { get; set; }
        public DateTime? DailyStatusUpdatedAt { get; set; }
        public string? ContactEmail { get; set; }
        public string? Phone { get; set; }
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
        // Gates appearance in public discovery (apex map / featured / Discover /
        // events). Does NOT gate subdomain resolution. New tenants start false.
        public bool IsPublished { get; set; }
        // First time this tenant was ever published (stamped by a trigger, never reset).
        // NULL = never published. Used by the stage->prod promotion import to refuse
        // overwriting a tenant that may hold real data.
        public DateTime? FirstPublishedAt { get; set; }
        public bool GiftCardsEnabled { get; set; } = false;
        public int GiftCardMinCents { get; set; } = 1000;       // $10 default
        public int GiftCardMaxCents { get; set; } = 50000;      // $500 default
        public bool RentalsEnabled { get; set; } = false;
        public bool ExtrasEnabled { get; set; } = true;
        public bool SeasonPassesEnabled { get; set; } = true;
        public bool ConcessionsEnabled { get; set; } = false;
        public bool BlogEnabled { get; set; } = false;
        // Stepped event-ticket price ladders (price rises by date or sales volume).
        // Gates CONFIGURING steps; already-configured ladders keep resolving.
        public bool DynamicPricingEnabled { get; set; } = false;
        // Race-entry tiers minting single-use share coupons at purchase. Gates both
        // configuring bundles and minting new codes at purchase time.
        public bool BundledCouponsEnabled { get; set; } = false;
        // When set, this tenant is a LoamPassMx track mapped to this LoamMx destination id.
        // NULL = not a LoamPassMx track. Super-admin controlled.
        public string? LoampassMxDestinationId { get; set; }
        // Deployment model (super-admin controlled): 'hosted' (default subdomain),
        // 'custom_domain', or 'embedded'. CustomDomain / Embed* hold the concrete config.
        public string ClientType { get; set; } = "hosted";
        public string? CustomDomain { get; set; }
        // Set true once the custom domain actually serves (DNS+TLS+resolution); gates
        // the subdomain->custom-domain redirect so we never forward to a dead domain.
        public bool CustomDomainVerified { get; set; }
        public bool EmbedEnabled { get; set; }
        // Origins (scheme + host) allowed to frame the embed widgets (CSP frame-ancestors).
        public string[]? EmbedAllowedOrigins { get; set; }
        // An embedded client's own website pages. Drive the subdomain redirect (home)
        // and the apex link targeting (events page, falling back to home).
        public string? ExternalHomeUrl { get; set; }
        public string? ExternalEventsUrl { get; set; }
        // Where an apex event click lands for an embedded client: 'external' (their
        // site) or 'ridepass' (the hosted {subdomain}.ridepass.io/Event/:id page).
        public string EmbedEventTarget { get; set; } = "external";
        public bool AllowSelfCancel { get; set; } = false;
        // Rider-facing headings for the gate-fee sections at checkout / event pricing.
        // NULL = platform defaults ("Rider Gate" / "Spectator Gate").
        public string? RiderGateLabel { get; set; }
        public string? SpectatorGateLabel { get; set; }
        public bool WaitlistEnabled { get; set; } = true;
        public int WaitlistConfirmWindowMinutes { get; set; } = 20;
        public bool MembershipEnabled { get; set; } = false;
        public string MembershipName { get; set; } = "Track Membership";
        public int MembershipPriceCents { get; set; } = 0;
        public string MembershipDurationKind { get; set; } = "yearly";   // 'one_time' | 'yearly'
        // Audience-shaped membership gates. Riders = race entries + day passes +
        // season passes. Spectators = extras (Gate Fees, camping, etc.).
        public bool MembershipRequiredForRiders { get; set; } = true;
        public bool MembershipRequiredForSpectators { get; set; } = false;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        // Set when the stage/local demo seeder has populated this tenant (hides the button). NULL = never.
        public DateTime? SeedDataPopulatedAt { get; set; }
    }
}
