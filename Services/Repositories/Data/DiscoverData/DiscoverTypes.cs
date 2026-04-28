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
    }

    public class EventDiscoverRow
    {
        public Guid EventId { get; set; }
        public Guid TenantId { get; set; }
        public string TenantSubdomain { get; set; } = null!;
        public string TenantDisplayName { get; set; } = null!;
        public string? TenantCity { get; set; }
        public string? TenantRegion { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? DistanceKm { get; set; }
        public string Title { get; set; } = null!;
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public string? LocationLabel { get; set; }
        public string EventTypeName { get; set; } = null!;
        public string EventTypeColor { get; set; } = null!;
    }
}
