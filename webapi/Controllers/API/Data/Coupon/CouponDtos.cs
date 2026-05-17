using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Coupon
{
    public class CouponResponse
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = null!;
        public string? Description { get; set; }
        public string DiscountKind { get; set; } = "percent";
        public int DiscountValue { get; set; }
        public string ApplicableScope { get; set; } = "all";
        public Guid? ApplicableEventId { get; set; }
        public DateTime? ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }
        public int? MaxTotalUses { get; set; }
        public int? MaxUsesPerUser { get; set; }
        public bool IsActive { get; set; }
        public int RedemptionCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UpsertCouponRequest
    {
        [Required, MaxLength(40), RegularExpression(@"^[A-Za-z0-9\-_]+$",
            ErrorMessage = "Code must be alphanumeric (with optional - or _).")]
        public string Code { get; set; } = null!;

        [MaxLength(200)]
        public string? Description { get; set; }

        [Required, RegularExpression("^(percent|amount)$")]
        public string DiscountKind { get; set; } = "percent";

        [Range(1, int.MaxValue)]
        public int DiscountValue { get; set; }

        [Required, RegularExpression("^(all|pass|event_ticket|season_pass)$")]
        public string ApplicableScope { get; set; } = "all";

        public Guid? ApplicableEventId { get; set; }
        public DateTime? ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }

        [Range(1, int.MaxValue)]
        public int? MaxTotalUses { get; set; }

        [Range(1, int.MaxValue)]
        public int? MaxUsesPerUser { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
