namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class DisputeListItem
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string TenantSubdomain { get; set; } = null!;
        public string Kind { get; set; } = null!; // "day_pass" | "event_ticket" | "unlinked"
        public Guid? PurchaseId { get; set; }
        public string? ItemName { get; set; }
        public string? PurchaserName { get; set; }
        public string? PurchaserEmail { get; set; }
        public string StripeDisputeId { get; set; } = null!;
        public string StripePaymentIntentId { get; set; } = null!;
        public string? StripeChargeId { get; set; }
        public long AmountCents { get; set; }
        public string Currency { get; set; } = null!;
        public string? Reason { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? EvidenceDueByUtc { get; set; }
        public DateTime StripeCreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
