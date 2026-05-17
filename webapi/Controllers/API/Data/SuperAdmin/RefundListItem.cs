namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class RefundListItem
    {
        public string Kind { get; set; } = null!; // "pass" | "event_ticket"
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string TenantSubdomain { get; set; } = null!;
        public string ItemName { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public int AmountCents { get; set; }
        public string? CancellationReason { get; set; }
        public DateTime? CancelledAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? StripePaymentIntentId { get; set; }
    }
}
