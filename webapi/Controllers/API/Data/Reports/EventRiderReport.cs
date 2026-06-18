namespace webapi.Controllers.API.Data.Reports
{
    public class EventRiderReportResponse
    {
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartsAtUtc { get; set; }
        public List<EventRiderRowDto> Rows { get; set; } = new();
        public int TotalRegistrants { get; set; }
        public int TotalCheckedIn { get; set; }
    }

    public class EventRiderRowDto
    {
        public Guid PurchaseId { get; set; }
        public string Source { get; set; } = null!;     // 'pass' | 'event_ticket' | 'season_pass'
        public string PurchaserName { get; set; } = null!;
        // Split for Trackside CSV export — derived from users.first/last_name
        // when available, falling back to a best-effort split of PurchaserName.
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public string? PurchaserPhone { get; set; }
        public string ItemName { get; set; } = null!;       // tier/pass/season-pass product name (the "Class" column)
        // For event_ticket rows: 'race_entry' | 'gate_fee'. NULL for pass/season_pass rows.
        public string? TierKind { get; set; }
        // For gate_fee rows: 'rider' | 'spectator'. NULL otherwise.
        public string? TierAudience { get; set; }
        // Per-purchase race number (event_ticket only). Falls back to user.race_number if NULL.
        public string? RaceNumber { get; set; }
        public string? UserRaceNumber { get; set; }         // profile fallback
        public string? Hometown { get; set; }               // for Trackside export
        public int Quantity { get; set; }
        public long AmountCents { get; set; }
        public string Status { get; set; } = null!;
        public bool CheckedIn { get; set; }
        public DateTime? CheckedInAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class DailyEventReportResponse
    {
        public string LocalDate { get; set; } = null!;        // 'YYYY-MM-DD' in tenant tz
        public List<DailyEventRowDto> Rows { get; set; } = new();
    }

    public class DailyEventRowDto
    {
        public Guid EventId { get; set; }
        public string Title { get; set; } = null!;
        public string EventTypeName { get; set; } = null!;
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public bool AllDay { get; set; }
        public int? Capacity { get; set; }
        public string Status { get; set; } = null!;
        public int Registered { get; set; }
        public int CheckedIn { get; set; }
        public long RevenueCents { get; set; }
    }
}
