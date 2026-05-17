using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Rental
{
    public class UpsertRentalProductRequest
    {
        [Required, MaxLength(140)]
        public string Name { get; set; } = null!;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [MaxLength(500)]
        public string? ImageUrl { get; set; }

        [Range(0, 10_000_000)]
        public int DailyRateCents { get; set; }

        [Range(0, 10_000_000)]
        public int DepositCents { get; set; }

        [Required, RegularExpression("^(pool|per_item)$")]
        public string TrackingKind { get; set; } = "pool";

        // Required (and only meaningful) when TrackingKind == 'pool'.
        [Range(1, 1000)]
        public int? InventoryPool { get; set; }

        public bool RequiresWaiver { get; set; } = true;

        [Range(0, 10000)]
        public int RiderPaidServiceChargeBps { get; set; } = 10000;

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; } = 100;
    }
}
