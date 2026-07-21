using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class UpsertShopTaxCategoryRequest
    {
        [Required, MaxLength(120)] public string Name { get; set; } = null!;
        [Range(0, 10000)] public int RateBps { get; set; }   // 825 = 8.25%
        public bool IsDefault { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
