using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class CreateTenantRequest
    {
        [Required, RegularExpression("^[a-z][a-z0-9-]{1,62}$",
            ErrorMessage = "Subdomain must be lowercase letters, digits, and hyphens. Start with a letter.")]
        public string Subdomain { get; set; } = null!;

        [Required, MaxLength(200)]
        public string DisplayName { get; set; } = null!;

        // Drives provisioning defaults at creation time. Defaults to motocross
        // since that's RidePass's original use case.
        [Required, RegularExpression("^(motocross|mountain_bike)$")]
        public string TenantType { get; set; } = "motocross";

        // MTB sub-classification driving seed defaults. Ignored for motocross.
        [RegularExpression("^(bike_park|shuttle|resort)$")]
        public string? VenueCategory { get; set; }

        [Required, MaxLength(80)]
        public string Timezone { get; set; } = "UTC";

        // Optional first tenant_admin provisioning.
        [EmailAddress, MaxLength(200)]
        public string? AdminEmail { get; set; }

        [MaxLength(120)]
        public string? AdminFirstName { get; set; }

        [MaxLength(120)]
        public string? AdminLastName { get; set; }

        // Deployment model + embed config (set at creation; editable later).
        [Required, RegularExpression("^(hosted|custom_domain|embedded)$")]
        public string ClientType { get; set; } = "hosted";
        [MaxLength(255)] public string? CustomDomain { get; set; }
        public bool EmbedEnabled { get; set; }
        public List<string>? EmbedAllowedOrigins { get; set; }

        public bool CustomDomainVerified { get; set; }
        [MaxLength(500)] public string? ExternalHomeUrl { get; set; }
        [MaxLength(500)] public string? ExternalEventsUrl { get; set; }
        [RegularExpression("^(external|ridepass)$")]
        public string EmbedEventTarget { get; set; } = "external";

        // Feature toggles. Defaults mirror the DB defaults (add-ons + season passes + waitlist on).
        public bool GiftCardsEnabled { get; set; }
        public bool ExtrasEnabled { get; set; } = true;
        public bool SeasonPassesEnabled { get; set; } = true;
        public bool ConcessionsEnabled { get; set; }
        public bool BikeShopEnabled { get; set; }
        public bool BlogEnabled { get; set; }
        public bool MembershipEnabled { get; set; }
        // Off by default: most tracks don't want a waitlist (Script0180).
        public bool WaitlistEnabled { get; set; } = false;
        public bool WaitlistPrepayEnabled { get; set; } = false;
        public bool AllowSelfCancel { get; set; }
        public bool DynamicPricingEnabled { get; set; }
        public bool BundledCouponsEnabled { get; set; }
    }
}
