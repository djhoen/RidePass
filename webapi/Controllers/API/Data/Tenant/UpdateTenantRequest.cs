using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    public class UpdateTenantRequest
    {
        [Required]
        [RegularExpression(@"^[A-Za-z_]+/[A-Za-z_+\-0-9]+(?:/[A-Za-z_+\-0-9]+)?$",
            ErrorMessage = "Timezone must be an IANA name like 'America/Denver'.")]
        public string Timezone { get; set; } = null!;

        public bool RequireReservationForPasses { get; set; }

        public bool RequireEmergencyContact { get; set; }

        public bool AllowEventSubscriptions { get; set; } = true;
    }

    public class UpdateTenantHomeContentRequest
    {
        public string? AboutHtml { get; set; }
        // hours_json is opaque on the wire — the frontend constructs the
        // {mon: {open: "09:00", close: "17:00"}, ...} structure.
        public string? HoursJson { get; set; }

        // Heading text for the events row on the public home page (null = "Next Up").
        public string? HomeNextUpTitle { get; set; }
        // Whitelist of event type IDs to surface in the row (null/empty = all).
        public Guid[]? HomeNextUpEventTypeIds { get; set; }

        // Benefits section rich-text content.
        public string? HomeBenefitsHtml { get; set; }
        // Per-section visibility map as JSON: { "sectionKey": bool }. Missing key = visible.
        public string? HomeSectionsJson { get; set; }
    }

    public class UpdateTenantDailyStatusRequest
    {
        // null = clear status. true = open. false = closed.
        public bool? Open { get; set; }
        [System.ComponentModel.DataAnnotations.MaxLength(280)]
        public string? Message { get; set; }
    }

    public class UpdateTenantFooterRequest
    {
        [System.ComponentModel.DataAnnotations.MaxLength(200)]
        public string? ContactEmail { get; set; }
        [System.ComponentModel.DataAnnotations.MaxLength(40)]
        public string? Phone { get; set; }
        public string? SocialFacebookUrl { get; set; }
        public string? SocialInstagramUrl { get; set; }
        public string? SocialTiktokUrl { get; set; }
        public string? SocialYoutubeUrl { get; set; }
        public string? RefundPolicyHtml { get; set; }
    }

    public class UpdateTenantLocationRequest
    {
        public string? ShippingName { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
