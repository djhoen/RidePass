namespace webapi.Controllers.API.Data.EventTicketTier
{
    public class EventTicketTierResponse
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public string Kind { get; set; } = "gate_fee";
        public string Audience { get; set; } = "rider";
        public bool Required { get; set; }
        public string Name { get; set; } = null!;
        public int PriceCents { get; set; }
        public int? Inventory { get; set; }
        public int? Sold { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }

        // Bundled coupons — tenant-configured perk on race-entry tiers. Riders see a
        // "Includes N coupons" badge during purchase; codes are auto-minted on payment.
        public int? BundledCouponCount { get; set; }
        public string? BundledCouponDiscountKind { get; set; }
        public int? BundledCouponDiscountValue { get; set; }
        public string? BundledCouponScope { get; set; }
        public int? BundledCouponExpiresInDays { get; set; }
    }
}
