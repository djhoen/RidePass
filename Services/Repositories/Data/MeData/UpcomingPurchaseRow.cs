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
        public bool RegistrationComplete { get; set; } = true;  // event_ticket: all the rider's entries registered (waiver may or may not be required)
        public bool WaiverSigned { get; set; }                  // event_ticket: at least one entry has a signed waiver on file
        /// <summary>event_ticket: this event requires a waiver for the audience this rider holds
        /// tickets for. Lets the card distinguish "unsigned" from "nothing to sign".</summary>
        public bool WaiverRequired { get; set; }
        public DateTime? OccursAtUtc { get; set; }
        // event_ticket: when the event ENDS. The card stays in "upcoming" until the day after this,
        // so a rider standing at the gate on race day doesn't find their ticket filed under "past".
        public DateTime? EndsAtUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }
        public int AmountCents { get; set; }
        public string? RedemptionToken { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
