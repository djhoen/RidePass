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

    // Admission/amusement tax collected on event tickets in a date range, for the tenant's remittance.
    public class AdmissionTaxTotals
    {
        public long TaxCollectedCents { get; set; }   // paid + redeemed
        public long TaxableSalesCents { get; set; }    // gross (tax-inclusive) of taxed rows
        public int TaxedTicketCount { get; set; }
        public long RefundedTaxCents { get; set; }     // tax on refunded rows
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
        /// <summary>Null on a no-event walk-up season pass admission (Script0236), which anchors
        /// to a calendar date instead of an event.</summary>
        public Guid? EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        /// <summary>Always set. For a walk-up admission this is its check_in_date read as midnight
        /// in the TENANT's zone, so the today/future split below still classifies it correctly.</summary>
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

    /// <summary>
    /// One registrant row on the date-range Rider Report (tickets + season-pass
    /// reservations across every event in the range), with the gate-day extras:
    /// linked wristband and waiver coverage.
    /// </summary>
    public class RiderReportRow
    {
        public Guid PurchaseId { get; set; }
        public string Source { get; set; } = null!;          // 'ticket' | 'season_pass'
        // NULL for a walk-up season-pass admission: it is anchored to a calendar date, not an
        // event (Script0236). EventStartsAtUtc is still always set (the event's start, or the
        // walk-up date read as midnight in the tenant's zone), so ordering never has to special-case it.
        public Guid? EventId { get; set; }
        public string? EventTitle { get; set; }
        public DateTime EventStartsAtUtc { get; set; }
        public string RiderName { get; set; } = null!;
        public string? Email { get; set; }
        public Guid? UserId { get; set; }
        public string ItemName { get; set; } = null!;
        public bool CheckedIn { get; set; }
        public DateTime? CheckedInAtUtc { get; set; }
        public string? WristbandCode { get; set; }
        public bool WaiverSigned { get; set; }
        // How this person got in, as one filterable bucket. See RiderPurchaseTypes.
        public string PurchaseType { get; set; } = null!;
        // The tenant's own name for the event type ("Lift Day", "Clinic"), plus the underlying
        // code so the UI can group renamed types (Highland calls a lesson a Clinic).
        public string? EventTypeName { get; set; }
        public string? EventTypeCode { get; set; }
        // Paid but never completed registration (rider details / waiver still outstanding).
        public bool RegistrationComplete { get; set; }
        // Rider's date of birth as captured on the purchase; drives the minor filter.
        public DateTime? RiderBirthdate { get; set; }
    }

    /// <summary>
    /// The buckets the Rider Report's purchase-type filter offers. Derived per row in SQL, so
    /// the value is stable across the report, the drill-in, and the CSV export.
    /// </summary>
    public static class RiderPurchaseTypes
    {
        public const string DayTicket = "day_ticket";
        public const string RaceEntry = "race_entry";
        public const string SeasonPassUnlimited = "season_pass_unlimited";
        public const string SeasonPassCredits = "season_pass_credits";
        public const string SeasonPassDays = "season_pass_days";
        public const string SpectatorPass = "spectator_pass";

        public static readonly string[] All =
        {
            DayTicket, RaceEntry, SeasonPassUnlimited, SeasonPassCredits, SeasonPassDays, SpectatorPass,
        };
    }

    /// <summary>One signed waiver on the rider drill-in.</summary>
    public class RiderWaiverRow
    {
        public Guid Id { get; set; }
        public string WaiverName { get; set; } = null!;
        public int WaiverVersion { get; set; }
        public DateTime SignedAtUtc { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentName { get; set; }
        public bool WaiverIsCurrent { get; set; }
        public string? SignerName { get; set; }
        // Whether a signature image exists to fetch. The image itself is pulled on demand
        // (Reports/Admin/RiderWaiver/{id}/Signature) so the drill-in payload stays small
        // however many waivers the rider has on file.
        public bool HasSignatureImage { get; set; }
    }

    /// <summary>
    /// Identity + lifetime activity for the rider drill-in header. Resolved by account id when
    /// the rider has one, else by the email on their purchases (guests never get a user row).
    /// </summary>
    public class RiderProfileRow
    {
        public Guid? UserId { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Hometown { get; set; }
        public string? RaceNumber { get; set; }
        public DateTime? Birthdate { get; set; }
        public DateTime? MemberSinceUtc { get; set; }
        public string? Bike { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? ParentGuardianName { get; set; }
        // Lifetime across this tenant: paid registrations, how many they actually attended,
        // what they've spent, and the bracketing visit dates.
        public int TotalRegistrations { get; set; }
        public int TotalCheckedIn { get; set; }
        public long TotalSpentCents { get; set; }
        public DateTime? FirstVisitUtc { get; set; }
        public DateTime? LastVisitUtc { get; set; }
    }
}
