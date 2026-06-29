using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionTaxCategoryRequest
    {
        [Required, MaxLength(80)]
        public string Name { get; set; } = null!;

        // Rate in basis points (825 = 8.25%). Capped at 100% server-side.
        [Range(0, 10000)]
        public int RateBps { get; set; }

        public bool IsDefault { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
