using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Pass
{
    public class UpsertPassProductRequest
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Range(1, 1_000_000)]
        public int PriceCents { get; set; }

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; } = 100;

        public bool RequiresWaiver { get; set; } = true;

        [Range(0, 10000)]
        public int RiderPaidServiceChargeBps { get; set; } = 10000;
    }
}
