using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class UpsertShopProductRequest
    {
        [Required, MaxLength(200)] public string Name { get; set; } = null!;
        [MaxLength(4000)] public string? Description { get; set; }
        [MaxLength(120)] public string? Brand { get; set; }
        [MaxLength(500)] public string? ImageUrl { get; set; }
        public Guid? CategoryId { get; set; }
        public Guid? SupplierId { get; set; }
        public bool IsSellable { get; set; } = true;
        /// <summary>List in the online store (distinct from IsSellable). Defaults true.</summary>
        public bool IsPublished { get; set; } = true;
        public bool IsRentable { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 100;
    }
}
