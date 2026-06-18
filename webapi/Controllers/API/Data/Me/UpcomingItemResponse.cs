namespace webapi.Controllers.API.Data.Me
{
    /// <summary>
    /// One row in the apex "what's coming up" feed. The frontend keys on
    /// <see cref="Kind"/> to pick the right card layout per category.
    /// </summary>
    public class UpcomingItemResponse
    {
        public string Kind { get; set; } = null!;       // event_ticket | pass | season_pass | membership
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string TenantSubdomain { get; set; } = null!;
        public string TenantDisplayName { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string? ImageUrl { get; set; }            // event cover; null for passes/memberships
        public string? TenantLogoUrl { get; set; }       // track logo for the card band
        public bool RegistrationComplete { get; set; } = true; // event_ticket: all entries registered + waivers signed
        public DateTime? OccursAtUtc { get; set; }       // event tickets, day passes
        public DateTime? ValidToUtc { get; set; }        // season passes, memberships
        public int AmountCents { get; set; }
        public string? RedemptionToken { get; set; }     // QR-able items only
        public DateTime CreatedAtUtc { get; set; }
    }
}
