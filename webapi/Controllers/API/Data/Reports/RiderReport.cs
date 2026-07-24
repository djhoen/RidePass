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
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = string.Empty;
        public DateTime EventStartsAtUtc { get; set; }
        public string RiderName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public Guid? UserId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public bool CheckedIn { get; set; }
        public DateTime? CheckedInAtUtc { get; set; }
        public string? WristbandCode { get; set; }
        public bool WaiverSigned { get; set; }
    }

    /// <summary>Rider drill-in: registrations (last year + upcoming) and signed waivers.</summary>
    public class RiderDetailResponse
    {
        public string RiderName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public List<RiderReportItem> Registrations { get; set; } = new();
        public List<RiderWaiverItem> Waivers { get; set; } = new();
    }

    public class RiderWaiverItem
    {
        public Guid Id { get; set; }
        public string WaiverName { get; set; } = string.Empty;
        public int WaiverVersion { get; set; }
        public DateTime SignedAtUtc { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentName { get; set; }
        public bool WaiverIsCurrent { get; set; }
    }
}
