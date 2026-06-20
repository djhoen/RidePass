using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.EventTicketTier
{
    public class UpsertEventTicketTierRequest
    {
        [Required, RegularExpression("^(race_entry|gate_fee)$",
            ErrorMessage = "Kind must be 'race_entry' or 'gate_fee'.")]
        public string Kind { get; set; } = "gate_fee";

        // gate_fee: 'rider' or 'spectator'. race_entry is always rider (server enforces).
        [Required, RegularExpression("^(rider|spectator)$",
            ErrorMessage = "Audience must be 'rider' or 'spectator'.")]
        public string Audience { get; set; } = "rider";

        // gate_fee only: a required gate fee must be bought by that audience (for a race,
        // a required rider gate fee forces "race class + one rider gate fee").
        public bool Required { get; set; }

        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;

        // 0 allowed for free kids entry / free gate fees.
        [Range(0, 1_000_000)]
        public int PriceCents { get; set; }

        [Range(1, int.MaxValue)]
        public int? Inventory { get; set; }

        public int SortOrder { get; set; } = 100;

        public bool IsActive { get; set; } = true;

        [Range(0, 10000)]
        public int RiderPaidServiceChargeBps { get; set; } = 10000;

        // Dynamic pricing (price steps). Steps sharing a LadderGroup on one event escalate
        // the price; the live price is the highest-priced step whose trigger has fired.
        // All triggers null = the base (starting) step. LadderGroup null = standalone tier.
        [MaxLength(64)] public string? LadderGroup { get; set; }
        [Range(0, int.MaxValue)] public int? MinSold { get; set; }            // quantity trigger
        [Range(0, 3650)]         public int? EffectiveDaysBefore { get; set; } // date trigger (relative)
        public DateTime? EffectiveAtUtc { get; set; }                          // date trigger (absolute)

        // Bundled-coupon config. Only meaningful when Kind = 'race_entry'. When
        // BundledCouponCount > 0 every paid purchase mints N coupon codes for the buyer.
        [Range(1, 100)] public int? BundledCouponCount { get; set; }
        [RegularExpression("^(percent|amount)$")]
        public string? BundledCouponDiscountKind { get; set; }
        [Range(1, int.MaxValue)] public int? BundledCouponDiscountValue { get; set; }
        [RegularExpression("^(all|pass|event_ticket|season_pass)$")]
        public string? BundledCouponScope { get; set; }
        [Range(1, 3650)] public int? BundledCouponExpiresInDays { get; set; }
    }
}
