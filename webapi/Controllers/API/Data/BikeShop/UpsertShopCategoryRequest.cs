using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class UpsertShopCategoryRequest
    {
        [Required, MaxLength(120)] public string Name { get; set; } = null!;
        public Guid? ParentId { get; set; }
        public int SortOrder { get; set; } = 100;
        public bool IsActive { get; set; } = true;
    }
}
