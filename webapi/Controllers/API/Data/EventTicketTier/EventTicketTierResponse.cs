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

        // Dynamic pricing (price steps). Echoed back so the admin editor can re-render the ladder.
        public string? LadderGroup { get; set; }
        public int? MinSold { get; set; }
        public int? EffectiveDaysBefore { get; set; }
        public DateTime? EffectiveAtUtc { get; set; }

        // Public buy-page messaging for a ladder's ACTIVE step (null for standalone tiers).
        // The public list collapses a ladder to its active step and fills these so the UI can
        // show "Only N left ... then $X" and/or a countdown to the next price.
        public int? RemainingToCapacity { get; set; }
        public int? NextPriceCents { get; set; }
        public string? NextChangeKind { get; set; }      // 'sold' | 'date'
        public int? NextChangeSoldThreshold { get; set; }
        public DateTime? NextChangeAtUtc { get; set; }

        // Bundled coupons — tenant-configured perk on race-entry tiers. Riders see a
        // "Includes N coupons" badge during purchase; codes are auto-minted on payment.
        public int? BundledCouponCount { get; set; }
        public string? BundledCouponDiscountKind { get; set; }
        public int? BundledCouponDiscountValue { get; set; }
        public string? BundledCouponScope { get; set; }
        public int? BundledCouponExpiresInDays { get; set; }
    }
}
