namespace Services.Repositories.Data.PaymentData
{
    public class EventTicketPurchase
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid TierId { get; set; }
        public Guid? PurchaserUserId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public int AmountCents { get; set; }
        public int ServiceChargeCents { get; set; }
        public Guid? AppliedRewardRedemptionId { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        public string Status { get; set; } = "pending";
        public string PurchaserEmail { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public Guid RedemptionToken { get; set; }
        public string? CancellationReason { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public string? RefundNote { get; set; }
        public DateTime? RedeemedAtUtc { get; set; }
        public Guid? RedeemedByUserId { get; set; }
        public Guid? SoldByUserId { get; set; }
        // Per-event race number assigned by staff at check-in time. NULL falls
        // back to the rider's profile race_number for displays/exports.
        public string? RaceNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EventTicketPurchaseWithContext : EventTicketPurchase
    {
        public string TierName { get; set; } = null!;
        public string TierKind { get; set; } = null!;   // 'spectator_pass' | 'race_entry'
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public string? EventDescription { get; set; }
        public string? EventLocationLabel { get; set; }
        public DateTime EventStartsAt { get; set; }
        public DateTime EventEndsAt { get; set; }
        public bool EventAllDay { get; set; }
    }
}
