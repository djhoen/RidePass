namespace Services.Repositories.Data.PaymentData
{
    /// <summary>
    /// One row from the v_recent_sales database view — a kind-tagged sale across
    /// any of the seven purchase tables (pass, event_ticket, event_extra,
    /// season_pass, membership, gift_card, rental). Used by the admin dashboard
    /// and the "all purchases" list so cross-cutting features don't have to
    /// UNION the tables themselves.
    /// </summary>
    public class RecentSalesItem
    {
        public string Kind { get; set; } = null!;
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Status { get; set; } = null!;
        public int AmountCents { get; set; }
        public Guid? PurchaserUserId { get; set; }
        public string? PurchaserEmail { get; set; }
        public string? PurchaserName { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public string? ItemName { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>The order's redemption token (the rider-facing "Order #"); null for
        /// kinds without one (membership, gift card).</summary>
        public Guid? RedemptionToken { get; set; }
    }
}
