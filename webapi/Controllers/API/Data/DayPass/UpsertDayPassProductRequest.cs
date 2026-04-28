using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.DayPass
{
    public class UpsertDayPassProductRequest
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Range(1, 1_000_000)]
        public int PriceCents { get; set; }

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; } = 100;
    }
}
