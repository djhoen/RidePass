namespace Services.Repositories.Data.RentalData
{
    public class RentalPurchase
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid ProductId { get; set; }
        public Guid? PurchaserUserId { get; set; }
        public string PurchaserEmail { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public Guid? WaiverSignatureId { get; set; }

        // Inclusive date range expressed in tenant tz at booking time.
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int Quantity { get; set; } = 1;
        public int DailyRateCentsFrozen { get; set; }
        public int DaysCount { get; set; }
        public int AmountCents { get; set; }
        public int ServiceChargeCents { get; set; }
        public int DepositCents { get; set; }

        public string? RentalPiId { get; set; }
        public string? DepositPiId { get; set; }
        // Set for direct charges: the tenant's connected account the rental was charged on.
        // NULL = platform charge. Drives the deposit refund onto the right account.
        public string? StripeConnectedAccountId { get; set; }
        public int DepositCapturedCents { get; set; }
        public Guid RedemptionToken { get; set; }
        // pending | paid | out | returned | damaged | cancelled | failed
        public string Status { get; set; } = "pending";
        public DateTime? CheckedOutAt { get; set; }
        public DateTime? ReturnedAt { get; set; }
        public string? ConditionNotes { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        public string? CancelledReason { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public Guid? AppliedRewardRedemptionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class RentalPurchaseItem
    {
        public Guid Id { get; set; }
        public Guid PurchaseId { get; set; }
        public Guid ItemId { get; set; }
        public string? CheckoutPhotoDataUrl { get; set; }
        public string? CheckoutNotes { get; set; }
        public string? ReturnPhotoDataUrl { get; set; }
        public string? ReturnNotes { get; set; }
    }

    public class RentalItemMaintenance
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid ItemId { get; set; }
        public DateTime StartsAtDate { get; set; }
        public DateTime EndsAtDate { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
