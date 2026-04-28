namespace Services.Repositories.Data.PaymentData
{
    public class Dispute
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? DayPassPurchaseId { get; set; }
        public Guid? EventTicketPurchaseId { get; set; }
        public string StripeDisputeId { get; set; } = null!;
        public string StripePaymentIntentId { get; set; } = null!;
        public string? StripeChargeId { get; set; }
        public long AmountCents { get; set; }
        public string Currency { get; set; } = null!;
        public string? Reason { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? EvidenceDueBy { get; set; }
        public DateTime StripeCreatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class DisputeWithContext : Dispute
    {
        public string TenantSubdomain { get; set; } = null!;
        public string? PurchaserName { get; set; }
        public string? PurchaserEmail { get; set; }
        public string? ItemName { get; set; }
    }
}
