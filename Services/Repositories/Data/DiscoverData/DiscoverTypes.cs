namespace Services.Repositories.Data.DiscoverData
{
    public class TrackDiscoverRow
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
        // Reuses the tenant's own hero image so super admins don't have to
        // upload a separate "track card" image. Falls back to a colored
        // placeholder on the client when null.
        public string? HeroImageUrl { get; set; }
    }

    public class EventDiscoverRow
    {
        public Guid EventId { get; set; }
        public Guid TenantId { get; set; }
        public string TenantSubdomain { get; set; } = null!;
        public string TenantDisplayName { get; set; } = null!;
        public string? TenantCity { get; set; }
        public string? TenantRegion { get; set; }
        // Logo to overlay on event card photos: the tenant's white logo when set,
        // otherwise their regular logo. Null when the tenant has neither.
        public string? TenantLogoUrl { get; set; }
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
        // Event-specific cover image; falls back to the event type's image on
        // the consumer when null. Both come from per-tenant uploads.
        public string? ImageUrl { get; set; }
        public string? EventTypeImageUrl { get; set; }
    }

    // One distinct system event-type (by code) seen across active tenants.
    // Powers the apex Events page filter so the client can list selectable
    // types without knowing each tenant's per-row type ids.
    public class EventTypeOptionRow
    {
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Color { get; set; } = null!;
    }
}

