namespace Services.Repositories.Data.PaymentData
{
    public class SeasonPassProduct
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public int PriceCents { get; set; }
        public DateTime ValidFromDate { get; set; }
        public DateTime ValidToDate { get; set; }
        public string Kind { get; set; } = "unlimited";       // unlimited | days_of_week | credits
        public int[]? ValidDaysOfWeek { get; set; }           // 0=Sun..6=Sat
        public int? TotalCredits { get; set; }
        public bool RequiresWaiver { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SeasonPassEventTypePerk
    {
        public Guid Id { get; set; }
        public Guid PassProductId { get; set; }
        public Guid EventTypeId { get; set; }
        public int DiscountPercent { get; set; }    // 100 = included
    }

    public class SeasonPassPurchase
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid PurchaserUserId { get; set; }
        public Guid ProductId { get; set; }
        public Guid? WaiverSignatureId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public int AmountCents { get; set; }
        public int ServiceChargeCents { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        public string Status { get; set; } = "pending";
        public string PurchaserEmail { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public Guid RedemptionToken { get; set; }
        public DateTime ValidFromDate { get; set; }
        public DateTime ValidToDate { get; set; }
        public int? CreditsRemaining { get; set; }
        public string? CancellationReason { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public string? RefundNote { get; set; }
        public string? PhotoDataUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SeasonPassPurchaseWithContext : SeasonPassPurchase
    {
        public string ProductName { get; set; } = null!;
        public string ProductKind { get; set; } = null!;
        public int? ProductTotalCredits { get; set; }
        public int[]? ProductValidDaysOfWeek { get; set; }
    }

    public class SeasonPassReservation
    {
        public Guid Id { get; set; }
        public Guid SeasonPassPurchaseId { get; set; }
        public Guid EventId { get; set; }
        public string Status { get; set; } = "reserved";    // reserved | checked_in | cancelled
        public DateTime ReservedAt { get; set; }
        public DateTime? CheckedInAt { get; set; }
        public DateTime? CancelledAt { get; set; }
    }

    public class SeasonPassReservationWithContext : SeasonPassReservation
    {
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartsAt { get; set; }
        public DateTime EventEndsAt { get; set; }
    }
}
