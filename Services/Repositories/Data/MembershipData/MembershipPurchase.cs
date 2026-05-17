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
        public string Status { get; set; } = "pending";
        public string? CancelledReason { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Guid? SoldByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
