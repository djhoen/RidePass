namespace webapi.Controllers.API.Data.Discover
{
    public class TrackDiscoverItem
    {
        public Guid TenantId { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? DistanceKm { get; set; }
        public int UpcomingEventsCount { get; set; }
        // Tenant's hero image, surfaced on the apex landing's track cards.
        // Relative path from /uploads/...; the consumer joins with the API
        // origin to absolutize. Null = render a colored placeholder.
        public string? HeroImageUrl { get; set; }
        // Front-door config so the apex track card links to the right destination.
        public string ClientType { get; set; } = "hosted";
        public string? CustomDomain { get; set; }
        public bool CustomDomainVerified { get; set; }
        public string? ExternalHomeUrl { get; set; }
        public string? ExternalEventsUrl { get; set; }
    }

    public class EventDiscoverItem
    {
        public Guid EventId { get; set; }
        public Guid TenantId { get; set; }
        public string TenantSubdomain { get; set; } = null!;
        public string TenantDisplayName { get; set; } = null!;
        public string? TenantCity { get; set; }
        public string? TenantRegion { get; set; }
        // Logo overlaid bottom-right on the apex event card photo: the tenant's white
        // logo when set, otherwise their regular logo. Null when they have neither.
        public string? TenantLogoUrl { get; set; }
        // Front-door config so the apex event card links to the right destination.
        public string TenantClientType { get; set; } = "hosted";
        public string? TenantCustomDomain { get; set; }
        public bool TenantCustomDomainVerified { get; set; }
        public string? TenantExternalHomeUrl { get; set; }
        public string? TenantExternalEventsUrl { get; set; }
        public string TenantEmbedEventTarget { get; set; } = "external";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? DistanceKm { get; set; }
        public string Title { get; set; } = null!;
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public string? LocationLabel { get; set; }
        public string EventTypeCode { get; set; } = null!;
        public string EventTypeName { get; set; } = null!;
        public string EventTypeColor { get; set; } = null!;
        // Event cover image when set; falls back to the event type's image
        // on the client. Either may be null, in which case the consumer
        // renders a colored placeholder using EventTypeColor.
        public string? ImageUrl { get; set; }
        public string? EventTypeImageUrl { get; set; }
    }

    // A selectable event type for the apex Events filter (distinct system code
    // across all active tenants).
    public class EventTypeOption
    {
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Color { get; set; } = null!;
    }

    // Result of the IP-based geolocation probe. CountryCode is an ISO 3166-1
    // alpha-2 code (e.g. "US"); null when the lookup could not resolve a country.
    // Latitude/Longitude are an approximate center the page can use to seed the
    // radius filter without prompting for the browser geolocation permission.
    public class GeoLocateResult
    {
        public string? CountryCode { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}

