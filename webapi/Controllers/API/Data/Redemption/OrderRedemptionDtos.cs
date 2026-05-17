namespace webapi.Controllers.API.Data.Redemption
{
    public class OrderLookupResponse
    {
        public Guid? StripePaymentIntentId { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public List<OrderItem> Items { get; set; } = new();
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
    }

    public class BulkRedeemRequest
    {
        public Guid OrderToken { get; set; }                 // any token from the order — used to authorize all-in-order
        public List<BulkRedeemItem> Items { get; set; } = new();
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
