namespace Services.Repositories.Data.PaymentData
{
    public class PassPurchase
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid PurchaserUserId { get; set; }
        public Guid ProductId { get; set; }
        public Guid? WaiverSignatureId { get; set; }
        public DateTime? ValidOnDate { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public int AmountCents { get; set; }
        public int ServiceChargeCents { get; set; }
        public Guid? AppliedRewardRedemptionId { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
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
        // Redemption audit (gate scan / pass check-in).
        public DateTime? RedeemedAtUtc { get; set; }
        public Guid? RedeemedByUserId { get; set; }
        // Counter-sale audit — null for self-purchases over the web.
        public Guid? SoldByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class PassPurchaseWithContext : PassPurchase
    {
        public string ProductName { get; set; } = null!;
    }
}
