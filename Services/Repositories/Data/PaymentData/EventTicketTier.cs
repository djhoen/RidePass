namespace Services.Repositories.Data.PaymentData
{
    public class EventTicketTier
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid EventId { get; set; }
        public string Kind { get; set; } = "spectator_pass"; // spectator_pass | race_entry
        public string Name { get; set; } = null!;
        public int PriceCents { get; set; }
        public int? Inventory { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }
        // Bundled coupons (race-entry tiers): when BundledCouponCount > 0, every paid
        // purchase of this tier auto-mints N coupons tied to the purchaser.
        public int? BundledCouponCount { get; set; }
        public string? BundledCouponDiscountKind { get; set; }     // percent | amount
        public int? BundledCouponDiscountValue { get; set; }       // bps if percent, cents if amount
        public string? BundledCouponScope { get; set; }            // all | pass | event_ticket | season_pass
        public int? BundledCouponExpiresInDays { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EventTicketTierSoldCount
    {
        public Guid TierId { get; set; }
        public int SoldCount { get; set; }
    }
}
