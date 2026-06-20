namespace Services.Repositories.Data.ReportData
{
    public class SalesTotals
    {
        public long RevenueCents { get; set; }
        public int SoldCount { get; set; }
        public int RefundedCount { get; set; }
        public int CancelledCount { get; set; }
        public long RefundedCents { get; set; }
    }

    public class DailyRevenuePoint
    {
        public string Date { get; set; } = null!;
        public long RevenueCents { get; set; }
        public int PassesSold { get; set; }
        public int TicketsSold { get; set; }
    }

    /// <summary>
    /// Gross sales for one revenue type within a period, from the unified tenant ledger
    /// (entry_kind='sale'). SourceKind is the ledger source: event_ticket, season_pass,
    /// membership, extras, rental, concession, etc. Gift-card purchases are intentionally
    /// absent (deferred revenue, recognized when the card is spent, so counting both the
    /// card purchase and the ticket it buys would double-count).
    /// </summary>
    public class RevenueByKindRow
    {
        public string SourceKind { get; set; } = null!;
        public long RevenueCents { get; set; }
        public int SaleCount { get; set; }
    }

    public class TopPassProductRow
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = null!;
        public int SoldCount { get; set; }
        public long RevenueCents { get; set; }
    }

    public class TopEventRow
    {
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartUtc { get; set; }
        public int SoldCount { get; set; }
        public long RevenueCents { get; set; }
    }

    public class PlatformSalesTotals
    {
        public long RevenueCents { get; set; }
        public int PassesSold { get; set; }
        public int TicketsSold { get; set; }
        public int RefundedCount { get; set; }
        public int DisputedCount { get; set; }
        public int TotalTenants { get; set; }
        public int ActiveTenants { get; set; }
    }

    public class TenantBreakdownRow
    {
        public Guid TenantId { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public int PassesSold { get; set; }
        public int TicketsSold { get; set; }
        public long RevenueCents { get; set; }
        public int RefundedCount { get; set; }
        public int DisputedCount { get; set; }
    }

    /// <summary>
    /// One row per registrant for an event. Source = 'pass' | 'event_ticket' | 'season_pass'.
    /// CheckedIn / CheckedInAtUtc come from the redemption fields on the underlying purchase row.
    /// </summary>
    public class EventRiderRow
    {
        public Guid PurchaseId { get; set; }
        public string Source { get; set; } = null!;          // 'pass' | 'event_ticket' | 'season_pass'
        public string PurchaserName { get; set; } = null!;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string PurchaserEmail { get; set; } = null!;
        public string? PurchaserPhone { get; set; }
        public string ItemName { get; set; } = null!;        // pass product / tier name / season pass name
        public string? TierKind { get; set; }                // event_ticket: 'race_entry' | 'gate_fee'
        public string? TierAudience { get; set; }            // gate_fee: 'rider' | 'spectator'
        public string? RaceNumber { get; set; }              // per-purchase race number
        public string? UserRaceNumber { get; set; }          // rider's profile race number (fallback)
        public string? Hometown { get; set; }                // user.city + region (best-effort)
        public int Quantity { get; set; }
        public long AmountCents { get; set; }
        public string Status { get; set; } = null!;          // pending | paid | cancelled | refunded | redeemed
        public bool CheckedIn { get; set; }
        public DateTime? CheckedInAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>
    /// Snapshot returned by a check-in token lookup. The token can come from any
    /// of pass / event ticket / season pass purchase rows; the rider is identified
    /// by the user_id on whichever row matched. Today + future registrations are
    /// gathered across all three sources for that user.
    /// </summary>
    public class CheckInLookup
    {
        public Guid? UserId { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public string? PurchaserPhone { get; set; }
        public string? PhotoDataUrl { get; set; }       // from season pass purchase if present
        public string MatchedTokenKind { get; set; } = null!;   // 'pass' | 'event_ticket' | 'season_pass'

        public bool RequiresWaiver { get; set; }
        public bool WaiverSigned { get; set; }
        public bool RequiresMembership { get; set; }
        public bool MembershipActive { get; set; }
        public string MembershipName { get; set; } = "Track Membership";

        public List<CheckInRegistration> TodayRegistrations { get; set; } = new();
        public List<CheckInRegistration> FutureRegistrations { get; set; } = new();
    }

    public class CheckInRegistration
    {
        public Guid Id { get; set; }                    // purchase or reservation id
        public string Source { get; set; } = null!;     // 'pass' | 'event_ticket' | 'season_pass'
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartsAtUtc { get; set; }
        public DateTime EventEndsAtUtc { get; set; }
        public string ItemName { get; set; } = null!;   // pass product / tier / season pass name
        public string Status { get; set; } = null!;
        public bool CheckedIn { get; set; }
        public DateTime? CheckedInAtUtc { get; set; }
        // Pass + event_ticket sources check in via POST /Redemption/Redeem/{token};
        // null for season_pass (which checks in via reservation id).
        public Guid? RedemptionToken { get; set; }
    }

    /// <summary>
    /// One event with aggregate counts for the Daily Events report.
    /// </summary>
    public class DailyEventRow
    {
        public Guid EventId { get; set; }
        public string Title { get; set; } = null!;
        public string EventTypeName { get; set; } = null!;
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public bool AllDay { get; set; }
        public int? Capacity { get; set; }
        public string Status { get; set; } = null!;
        // Aggregates summed across pass + ticket + season-pass-reservation rows.
        public int Registered { get; set; }
        public int CheckedIn { get; set; }
        public long RevenueCents { get; set; }
    }
}
