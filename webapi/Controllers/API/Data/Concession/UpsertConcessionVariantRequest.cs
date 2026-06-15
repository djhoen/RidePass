using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    public class UpsertConcessionVariantRequest
    {
        [MaxLength(60)]
        public string? Size { get; set; }

        [MaxLength(60)]
        public string? Color { get; set; }

        // null = use the product's base price.
        [Range(0, int.MaxValue)]
        public int? PriceCents { get; set; }

        public string? ImageUrl { get; set; }

        // null = unlimited stock.
        [Range(0, int.MaxValue)]
        public int? Inventory { get; set; }

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
    }
}
