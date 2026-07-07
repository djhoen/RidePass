namespace Services.Repositories.Data.GiftCardData
{
    public class GiftCard
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Code { get; set; } = null!;
        public int InitialAmountCents { get; set; }
        public int BalanceCents { get; set; }
        public Guid? BuyerUserId { get; set; }
        public string BuyerName { get; set; } = null!;
        public string BuyerEmail { get; set; } = null!;
        public string RecipientName { get; set; } = null!;
        public string RecipientEmail { get; set; } = null!;
        public string? PersonalNote { get; set; }
        public string DeliveryStatus { get; set; } = "pending";   // pending | delivered | failed
        public DateTime? ScheduledDeliveryAtUtc { get; set; }
        public DateTime? DeliveredAtUtc { get; set; }
        public string Status { get; set; } = "active";            // active | depleted | refunded
        public string? StripePaymentIntentId { get; set; }
        public string? StripeConnectedAccountId { get; set; }     // direct-charge account; NULL = platform
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class GiftCardRedemption
    {
        public Guid Id { get; set; }
        public Guid GiftCardId { get; set; }
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public string SourceKind { get; set; } = null!;  // pass | event_ticket | season_pass
        public Guid SourceId { get; set; }
        public int AmountCents { get; set; }
        public DateTime RedeemedAt { get; set; }
    }

    /// <summary>
    /// Result of resolving a gift-card code at checkout: the card itself plus the
    /// chunk that applies to this purchase (capped at remaining balance and at
    /// the post-discount line total).
    /// </summary>
    public class GiftCardApplication
    {
        public GiftCard Card { get; set; } = null!;
        public int AmountToApplyCents { get; set; }
    }
}
