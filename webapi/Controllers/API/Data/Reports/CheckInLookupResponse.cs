namespace webapi.Controllers.API.Data.Reports
{
    public class CheckInLookupResponse
    {
        public Guid? UserId { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public string? PurchaserPhone { get; set; }
        public string? PhotoDataUrl { get; set; }
        public string MatchedTokenKind { get; set; } = null!;

        // Gating flags surfaced to the UI so it can warn the gate staff before
        // they hit Check-In. The check-in endpoint enforces them server-side too.
        public bool RequiresWaiver { get; set; }
        public bool WaiverSigned { get; set; }
        public bool RequiresMembership { get; set; }
        public bool MembershipActive { get; set; }
        public string MembershipName { get; set; } = "Track Membership";

        public List<CheckInRegistrationDto> TodayRegistrations { get; set; } = new();
        public List<CheckInRegistrationDto> FutureRegistrations { get; set; } = new();
    }

    public class CheckInRegistrationDto
    {
        public Guid Id { get; set; }
        public string Source { get; set; } = null!;     // 'pass' | 'event_ticket' | 'season_pass'
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartsAtUtc { get; set; }
        public DateTime EventEndsAtUtc { get; set; }
        public string ItemName { get; set; } = null!;
        public string Status { get; set; } = null!;
        public bool CheckedIn { get; set; }
        public DateTime? CheckedInAtUtc { get; set; }
        // Pass + ticket: token to POST to /Redemption/Redeem/{token}.
        // Season pass: null (use Id with /SeasonPass/Reservation/{id}/CheckIn).
        public Guid? RedemptionToken { get; set; }
    }
}
