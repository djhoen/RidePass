namespace webapi.Controllers.API.Data.Redemption
{
    public class OrderLookupResponse
    {
        public Guid? StripePaymentIntentId { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public List<OrderItem> Items { get; set; } = new();
        // When true, the tenant requires gate staff to verify the rider's photo ID
        // against PurchaserName before redeeming. Drives the attestation gate in the UI;
        // also enforced server-side on RedeemBulk.
        public bool RequireIdAtCheckin { get; set; }
    }

    public class OrderItem
    {
        // 'pass' | 'event_ticket' | 'extras' | 'membership'
        public string Kind { get; set; } = null!;
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        public string ItemName { get; set; } = null!;
        public int AmountCents { get; set; }
        public string Status { get; set; } = null!;        // 'paid' | 'redeemed' | 'cancelled' | ...
        public bool IsRedeemableToday { get; set; }
        public string? NotRedeemableReason { get; set; }
        public DateTime? RedeemedAtUtc { get; set; }
        public string? RedeemedByName { get; set; }
        // For deferred-checkout event tickets: false when the rider hasn't finished
        // registration (rider details + required waiver). The gate surfaces this as a
        // warning so staff can collect the waiver before letting them on track.
        public bool RegistrationComplete { get; set; } = true;
        // The rider this ticket is for (when captured at registration), so a multi-rider
        // order reads clearly at the gate. Falls back to null when not yet registered.
        public string? AttendeeName { get; set; }
        // When the rider is a minor whose waiver was signed by a parent/guardian, the gate
        // shows who signed on their behalf.
        public bool SignedByParent { get; set; }
        public string? GuardianName { get; set; }
    }

    public class BulkRedeemRequest
    {
        public Guid OrderToken { get; set; }                 // any token from the rider's order — authorizes the event+purchaser set
        public List<BulkRedeemItem> Items { get; set; } = new();
        // Gate-staff attestation that the rider's photo ID was checked against the
        // purchaser name. Required only when the tenant has RequireIdAtCheckin on.
        public bool IdVerified { get; set; }
    }

    public class BulkRedeemItem
    {
        public string Kind { get; set; } = null!;            // 'pass' | 'event_ticket' | 'extras'
        public Guid PurchaseId { get; set; }
    }

    public class BulkRedeemResponse
    {
        public int RedeemedCount { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
