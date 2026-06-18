namespace Services.Repositories.Data.MeData
{
    /// <summary>
    /// Polymorphic row returned by the cross-tenant "what's coming up for
    /// this user" query. Each `Kind` ('event_ticket', 'pass', 'season_pass',
    /// 'membership') populates a subset of the temporal fields:
    ///   • event_ticket  → OccursAtUtc (event start)
    ///   • pass          → OccursAtUtc (valid_on_date at midnight UTC)
    ///   • season_pass   → ValidToUtc (range end)
    ///   • membership    → ValidToUtc (range end; may be null for lifetime)
    /// The frontend renders kind-aware cards from this single shape.
    /// </summary>
    public class UpcomingPurchaseRow
    {
        public string Kind { get; set; } = null!;
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string TenantSubdomain { get; set; } = null!;
        public string TenantDisplayName { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string? ImageUrl { get; set; }     // event cover (or event-type default); null for passes/memberships
        public string? TenantLogoUrl { get; set; } // track logo overlaid on the card band
        public bool RegistrationComplete { get; set; } = true;  // event_ticket: all the rider's entries registered + waivers signed
        public DateTime? OccursAtUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }
        public int AmountCents { get; set; }
        public string? RedemptionToken { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
