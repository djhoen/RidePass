using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class UpsertShopSupplierRequest
    {
        [Required, MaxLength(160)] public string Name { get; set; } = null!;
        [MaxLength(160)] public string? ContactName { get; set; }
        [MaxLength(200)] public string? Email { get; set; }
        [MaxLength(40)] public string? Phone { get; set; }
        [MaxLength(2000)] public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
