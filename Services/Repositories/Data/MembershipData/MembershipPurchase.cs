namespace Services.Repositories.Data.MembershipData
{
    public class MembershipPurchase
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public string NameAtPurchase { get; set; } = null!;
        public int PriceCents { get; set; }
        public string DurationKind { get; set; } = null!;        // 'one_time' | 'yearly'
        public DateTime ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }                // null = lifetime
        public int AmountCents { get; set; }
        public int ServiceChargeCents { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        public string? StripePaymentIntentId { get; set; }
        // Set for direct charges (bundled onto a direct event-ticket cart): the connected account
        // this row was charged on. NULL = platform charge. Drives refunds onto the right account.
        public string? StripeConnectedAccountId { get; set; }
        public string Status { get; set; } = "pending";
        public string? CancelledReason { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Guid? SoldByUserId { get; set; }

        // Staff-applied discount snapshot (Script0257). PriceCents stays the list price and
        // AmountCents becomes what was actually charged, so the two together plus this explain the gap.
        public int DiscountCents { get; set; }
        public Guid? DiscountPresetId { get; set; }
        public string? DiscountLabel { get; set; }
        public Guid? DiscountAuthorizedByUserId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
