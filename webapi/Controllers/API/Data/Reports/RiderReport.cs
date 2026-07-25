namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>Date-range Rider Report (Admission > Rider Report).</summary>
    public class RiderReportResponse
    {
        public List<RiderReportItem> Rows { get; set; } = new();
        /// <summary>True when the range matched more rows than the server cap; narrow the range.</summary>
        public bool Truncated { get; set; }
        public int TotalRows { get; set; }
        public int TotalCheckedIn { get; set; }
        public int TotalMissingWaiver { get; set; }
    }

    public class RiderReportItem
    {
        public Guid PurchaseId { get; set; }
        /// <summary>"ticket" | "season_pass".</summary>
        public string Source { get; set; } = string.Empty;
        /// <summary>Null for a walk-up season-pass admission (anchored to a date, not an event).</summary>
        public Guid? EventId { get; set; }
        /// <summary>Null for a walk-up admission; the UI labels those rows itself.</summary>
        public string? EventTitle { get; set; }
        public DateTime EventStartsAtUtc { get; set; }
        public string RiderName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public Guid? UserId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public bool CheckedIn { get; set; }
        public DateTime? CheckedInAtUtc { get; set; }
        public string? WristbandCode { get; set; }
        public bool WaiverSigned { get; set; }
        /// <summary>One of RiderPurchaseTypes: how this person got in (day ticket, race entry,
        /// season pass by kind, spectator pass).</summary>
        public string PurchaseType { get; set; } = string.Empty;
        /// <summary>The tenant's own label for the event type ("Lift Day", "Clinic").</summary>
        public string? EventTypeName { get; set; }
        /// <summary>Underlying event-type code (open_ride, lesson, race, camp, ...), stable across renames.</summary>
        public string? EventTypeCode { get; set; }
        /// <summary>False when the entry was paid for but rider details were never completed.</summary>
        public bool RegistrationComplete { get; set; }
        /// <summary>Rider's age at the event, when a birthdate was captured. Drives the minor filter.</summary>
        public int? AgeAtEvent { get; set; }
    }

    /// <summary>Rider drill-in: profile, registrations (last year + upcoming), and signed waivers.</summary>
    public class RiderDetailResponse
    {
        public string RiderName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public RiderProfileItem? Profile { get; set; }
        public List<RiderReportItem> Registrations { get; set; } = new();
        public List<RiderWaiverItem> Waivers { get; set; } = new();
    }

    /// <summary>Identity + lifetime activity shown at the top of the rider drill-in.</summary>
    public class RiderProfileItem
    {
        public Guid? UserId { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Hometown { get; set; }
        public string? RaceNumber { get; set; }
        public DateTime? BirthdateUtc { get; set; }
        public int? Age { get; set; }
        public DateTime? MemberSinceUtc { get; set; }
        public string? Bike { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? ParentGuardianName { get; set; }
        public int TotalRegistrations { get; set; }
        public int TotalCheckedIn { get; set; }
        public long TotalSpentCents { get; set; }
        public DateTime? FirstVisitUtc { get; set; }
        public DateTime? LastVisitUtc { get; set; }
        /// <summary>True when this rider has no account (guest checkout only), so there's no
        /// customer profile to link to and the details came off their purchases.</summary>
        public bool IsGuest { get; set; }
    }

    public class RiderWaiverItem
    {
        public Guid Id { get; set; }
        public string WaiverName { get; set; } = string.Empty;
        public int WaiverVersion { get; set; }
        public DateTime SignedAtUtc { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentName { get; set; }
        public string? SignerName { get; set; }
        public bool WaiverIsCurrent { get; set; }
        /// <summary>True when a signature image can be fetched for this row; the image itself
        /// comes from Reports/Admin/RiderWaiver/{id}/Signature on demand.</summary>
        public bool HasSignatureImage { get; set; }
    }
}
