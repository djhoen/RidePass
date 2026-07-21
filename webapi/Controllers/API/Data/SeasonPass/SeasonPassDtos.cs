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

        // Per-event-type perks (free entry or % off). Superseded by Benefits — still accepted so
        // an older client keeps working, but Benefits wins when both are sent.
        public List<EventTypePerkInput> Perks { get; set; } = new();

        /// <summary>Everything this pass grants: event discounts today, F&amp;B / rentals / buddy
        /// passes as those surfaces are wired.</summary>
        public List<SeasonPassBenefitInput> Benefits { get; set; } = new();
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

        /// <summary>What this pass grants, resolved with display names for the landing page.</summary>
        public List<SeasonPassBenefitInput> Benefits { get; set; } = new();
    }

    public class BuySeasonPassRequest
    {
        /// <summary>
        /// The order: one entry per product, with how many passes of it to buy. One buyer can
        /// hold several passes for other riders, so this is a cart rather than a single product
        /// id. Holder details, photos, and waivers are collected after payment.
        /// </summary>
        [Required, MinLength(1)] public List<SeasonPassCartItem> Items { get; set; } = new();

        [MaxLength(40)] public string? CouponCode { get; set; }
        [MaxLength(40)] public string? GiftCardCode { get; set; }
    }

    public class BuySeasonPassResponse
    {
        /// <summary>Every pass created by this order, in the sequence the client should register them.</summary>
        public List<SeasonPassPurchaseItem> Passes { get; set; } = new();
        public string ClientSecret { get; set; } = string.Empty;

        /// <summary>What the card is actually charged: order total minus any gift card applied.</summary>
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

        /// <summary>Whether this pass's product needs a signed waiver — drives the finish-registration form.</summary>
        public bool RequiresWaiver { get; set; }

        /// <summary>
        /// False when the pass is paid but has no holder / photo / waiver yet, which the gate
        /// refuses. Lets the rider's pass list prompt them to finish — the recovery path when a
        /// redirect-based payment took them out of checkout before the registration step.
        /// </summary>
        public bool RegistrationComplete { get; set; }
        public string? HolderFirstName { get; set; }
        public string? HolderLastName { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
