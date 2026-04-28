namespace Services.Repositories.Data.PaymentData
{
    public class DayPassPurchase
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid PurchaserUserId { get; set; }
        public Guid ProductId { get; set; }
        public Guid? WaiverSignatureId { get; set; }
        public DateTime? ValidOnDate { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public int AmountCents { get; set; }
        public string Status { get; set; } = "pending";
        public string PurchaserEmail { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public Guid RedemptionToken { get; set; }
        public Guid? EventId { get; set; }
        public int Quantity { get; set; } = 1;
        public string? CancellationReason { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public string? RefundNote { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class DayPassPurchaseWithContext : DayPassPurchase
    {
        public string ProductName { get; set; } = null!;
    }
}
