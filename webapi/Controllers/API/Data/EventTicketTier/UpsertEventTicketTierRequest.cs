using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.EventTicketTier
{
    public class UpsertEventTicketTierRequest
    {
        [Required, RegularExpression("^(spectator_pass|race_entry)$",
            ErrorMessage = "Kind must be 'spectator_pass' or 'race_entry'.")]
        public string Kind { get; set; } = "spectator_pass";

        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;

        [Range(1, 1_000_000)]
        public int PriceCents { get; set; }

        [Range(1, int.MaxValue)]
        public int? Inventory { get; set; }

        public int SortOrder { get; set; } = 100;

        public bool IsActive { get; set; } = true;

        [Range(0, 10000)]
        public int RiderPaidServiceChargeBps { get; set; } = 10000;

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
