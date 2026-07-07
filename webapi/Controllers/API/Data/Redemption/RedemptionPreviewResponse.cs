namespace webapi.Controllers.API.Data.Redemption
{
    public class RedemptionPreviewResponse
    {
        public string Kind { get; set; } = null!; // "pass" | "event_ticket"
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public string ItemName { get; set; } = null!; // product name or "Event Title: Tier"
        public int AmountCents { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? ValidOnDate { get; set; }

        // Event detail fields (populated only when Kind == "event_ticket")
        public string? EventTitle { get; set; }
        public string? TierName { get; set; }
        public string? EventDescription { get; set; }
        public string? EventLocationLabel { get; set; }
        public DateTime? EventStartsAtUtc { get; set; }
        public DateTime? EventEndsAtUtc { get; set; }
        public bool EventAllDay { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        // Whether today (in tenant timezone) is within the valid redemption window.
        public bool IsRedeemableToday { get; set; }
        public string? NotRedeemableReason { get; set; }

        // For deferred-checkout event tickets: false when the rider hasn't finished
        // registration (rider details + required waiver). Surfaced as a gate warning.
        public bool RegistrationComplete { get; set; } = true;
        public string? RaceNumber { get; set; }
        // The rider this ticket is for, and, when they're a minor, the parent/guardian who
        // signed their waiver so the gate can display it.
        public string? AttendeeName { get; set; }
        public bool SignedByParent { get; set; }
        public string? GuardianName { get; set; }
    }
}
