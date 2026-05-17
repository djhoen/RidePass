using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    public class UpsertSeasonPassProductRequest
    {
        [Required, MaxLength(140)]
        public string Name { get; set; } = null!;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Range(1, 10_000_000)]
        public int PriceCents { get; set; }

        [Required]
        public DateTime ValidFromDate { get; set; }

        [Required]
        public DateTime ValidToDate { get; set; }

        [Required, RegularExpression("^(unlimited|days_of_week|credits)$")]
        public string Kind { get; set; } = "unlimited";

        public int[]? ValidDaysOfWeek { get; set; }    // 0-6, Sun=0

        [Range(1, 1000)]
        public int? TotalCredits { get; set; }

        public bool RequiresWaiver { get; set; } = true;

        [Range(0, 10000)]
        public int RiderPaidServiceChargeBps { get; set; } = 10000;

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; } = 100;

        // Per-event-type perks (free entry or % off). Stored alongside the product.
        public List<EventTypePerkInput> Perks { get; set; } = new();
    }

    public class EventTypePerkInput
    {
        [Required] public Guid EventTypeId { get; set; }
        [Range(0, 100)] public int DiscountPercent { get; set; }
    }

    public class SeasonPassProductResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public int PriceCents { get; set; }
        public DateTime ValidFromDate { get; set; }
        public DateTime ValidToDate { get; set; }
        public string Kind { get; set; } = null!;
        public int[]? ValidDaysOfWeek { get; set; }
        public int? TotalCredits { get; set; }
        public bool RequiresWaiver { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public List<EventTypePerkInput> Perks { get; set; } = new();
    }

    public class BuySeasonPassRequest
    {
        [Required] public Guid ProductId { get; set; }
        [Required] public string PhotoDataUrl { get; set; } = null!;
        [System.ComponentModel.DataAnnotations.MaxLength(40)]
        public string? CouponCode { get; set; }

        [System.ComponentModel.DataAnnotations.MaxLength(40)]
        public string? GiftCardCode { get; set; }
    }

    public class BuySeasonPassResponse
    {
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        public string ClientSecret { get; set; } = string.Empty;
        public int AmountCents { get; set; }
        public int RiderServiceChargeCents { get; set; }
        public int GiftCardAppliedCents { get; set; }
    }

    public class SeasonPassReserveRequest
    {
        [Required] public Guid PassPurchaseId { get; set; }
        [Required] public Guid EventId { get; set; }
    }

    public class MySeasonPassResponse
    {
        public Guid Id { get; set; }
        public Guid RedemptionToken { get; set; }
        public string ProductName { get; set; } = null!;
        public string ProductKind { get; set; } = null!;
        public int? CreditsRemaining { get; set; }
        public int[]? ValidDaysOfWeek { get; set; }
        public DateTime ValidFromDate { get; set; }
        public DateTime ValidToDate { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }
}
