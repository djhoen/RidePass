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

        // ── Training group (docs/lessons.md) ─────────────────────────────────────────
        // A coached group is a tier with a coach attached. All optional; an ordinary tier
        // leaves them null and behaves exactly as before.
        public Guid? InstructorId { get; set; }
        // Ability + equipment bands. Free text on purpose: MX segments by skill plus
        // displacement, MTB by trail-difficulty ability zone. The UI supplies a picklist.
        [MaxLength(60)] public string? SkillLevel { get; set; }
        [MaxLength(60)] public string? EquipmentLabel { get; set; }
        // The group's own window inside the event. Both null = inherit the event's window.
        public DateTime? StartsAt { get; set; }
        public DateTime? EndsAt { get; set; }

        // ── Party pricing ("up to N riders, one price") ──────────────────────────────
        // Defaults are ordinary per-person pricing. Each rider still gets their own ticket;
        // only the price varies by position (Services.Pricing.PartyPricing).
        [Range(1, 50)] public int PartySizeIncluded { get; set; } = 1;
        [Range(0, 1_000_000)] public int? PartyPriceCents { get; set; }
        [Range(1, 50)] public int? PartySizeMax { get; set; }
    }
}
