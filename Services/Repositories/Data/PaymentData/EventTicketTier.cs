namespace Services.Repositories.Data.PaymentData
{
    public class EventTicketTier
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid EventId { get; set; }
        public string Kind { get; set; } = "gate_fee"; // race_entry | gate_fee (spectator_pass = legacy, converted to gate_fee)
        // Gate fees pick an audience; race_entry is always rider. 'rider' | 'spectator'.
        public string Audience { get; set; } = "rider";
        // gate_fee only: when true, a buyer of that audience must purchase one. For a
        // race, a required rider gate fee forces "race class + one rider gate fee".
        public bool Required { get; set; }
        public string Name { get; set; } = null!;
        public int PriceCents { get; set; }
        public int? Inventory { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }
        // Dynamic pricing: steps sharing a LadderGroup on one event escalate the price.
        // LadderGroup NULL = standalone tier (default). A step "fires" when its trigger is
        // met; the live price is the highest-priced fired step. All triggers NULL = base step.
        public string? LadderGroup { get; set; }
        public int? MinSold { get; set; }              // quantity trigger: group cumulative sold >= this
        public int? EffectiveDaysBefore { get; set; }  // date trigger: fires at event start minus N days
        public DateTime? EffectiveAtUtc { get; set; }   // date trigger: fires at/after this instant (absolute)
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
