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
        // Null on imported cards (legacy balances usually arrive as code + amount only); the buy
        // flow always sets all four.
        public string? BuyerName { get; set; }
        public string? BuyerEmail { get; set; }
        public string? RecipientName { get; set; }
        public string? RecipientEmail { get; set; }
        public string? PersonalNote { get; set; }
        public string DeliveryStatus { get; set; } = "pending";   // pending | delivered | failed
        public DateTime? ScheduledDeliveryAtUtc { get; set; }
        public DateTime? DeliveredAtUtc { get; set; }
        public string Status { get; set; } = "active";            // pending | active | depleted | refunded | void
        public string? StripePaymentIntentId { get; set; }
        public string? StripeConnectedAccountId { get; set; }     // direct-charge account; NULL = platform
        // Set only on cards brought in from a previous system (no Stripe PI, no delivery email,
        // excluded from the Purchases feed).
        public string? ImportedFrom { get; set; }
        public DateTime? ImportedAt { get; set; }
        public Guid? ImportedByUserId { get; set; }
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
