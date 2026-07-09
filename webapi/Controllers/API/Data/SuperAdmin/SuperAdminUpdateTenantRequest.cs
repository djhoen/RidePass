using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SuperAdmin
{
    /// <summary>
    /// Super-admin edit of a tenant's core details. Subdomain and tenant type are
    /// intentionally NOT editable here (identity / provisioning-locked).
    /// Named distinctly from the tenant-facing UpdateTenantRequest so Swagger's
    /// short-name schema IDs don't collide.
    /// </summary>
    public class SuperAdminUpdateTenantRequest
    {
        [Required, MaxLength(200)]
        public string DisplayName { get; set; } = null!;

        [Required, RegularExpression("^(active|suspended|pending)$")]
        public string Status { get; set; } = null!;

        [Required, MaxLength(64)]
        public string Timezone { get; set; } = null!;

        // Whether the tenant appears in public discovery (map / featured / search / events).
        public bool IsPublished { get; set; }

        // Platform service charge in basis points (0..10000 = 0%..100%).
        [Range(0, 10000)]
        public int ServiceChargeBps { get; set; }

        // Monthly cap in cents; null = no cap.
        public int? MonthlyServiceChargeCapCents { get; set; }

        // 'platform' = charge on RidePass's account, internal split, monthly payout.
        // 'direct' = charge on the tenant's own connected Stripe account with our service fee as the
        // Stripe application fee (required for tenants over the $1M/yr card-network sub-merchant cap).
        [RegularExpression("^(platform|direct)$")]
        public string StripeChargeMode { get; set; } = "platform";

        [MaxLength(300)] public string? AddressLine { get; set; }
        [MaxLength(120)] public string? City { get; set; }
        [MaxLength(120)] public string? Region { get; set; }
        [MaxLength(40)] public string? PostalCode { get; set; }
        [MaxLength(80)] public string? Country { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [MaxLength(200)] public string? ContactEmail { get; set; }
        [MaxLength(40)] public string? Phone { get; set; }

        // LoamMx destination id. Non-empty marks this tenant as a LoamPassMx track; blank clears it.
        [MaxLength(64)] public string? LoampassMxDestinationId { get; set; }

        // Deployment model: how the track's public presence is delivered.
        [Required, RegularExpression("^(hosted|custom_domain|embedded)$")]
        public string ClientType { get; set; } = "hosted";

        // The track's own domain (host only, e.g. "www.xyztrack.com"), for client_type = custom_domain.
        [MaxLength(255)] public string? CustomDomain { get; set; }

        // Embed widgets enabled, and the origins allowed to frame them (CSP frame-ancestors).
        public bool EmbedEnabled { get; set; }
        public List<string>? EmbedAllowedOrigins { get; set; }

        // Custom domain is "live" — gates the subdomain->custom-domain redirect.
        public bool CustomDomainVerified { get; set; }
        // An embedded client's own website pages (subdomain redirect + apex link targeting).
        [MaxLength(500)] public string? ExternalHomeUrl { get; set; }
        [MaxLength(500)] public string? ExternalEventsUrl { get; set; }
        // Where an apex event click lands for an embedded client.
        [RegularExpression("^(external|ridepass)$")]
        public string EmbedEventTarget { get; set; } = "external";

        // Tenant-level feature toggles (Feature Toggles tab).
        public bool GiftCardsEnabled { get; set; }
        public bool RentalsEnabled { get; set; }
        public bool ExtrasEnabled { get; set; }
        public bool SeasonPassesEnabled { get; set; }
        public bool ConcessionsEnabled { get; set; }
        public bool BlogEnabled { get; set; }
        public bool MembershipEnabled { get; set; }
        public bool WaitlistEnabled { get; set; }
        public bool AllowSelfCancel { get; set; }
        public bool DynamicPricingEnabled { get; set; }
        public bool BundledCouponsEnabled { get; set; }
    }
}
