using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    public class UpsertConcessionProductRequest
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [RegularExpression("^(food|drink|swag|other)$")]
        public string Category { get; set; } = "other";

        [Range(0, int.MaxValue)]
        public int PriceCents { get; set; }

        public string? ImageUrl { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
    }
}
