using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class UpsertPurchaseOrderRequest
    {
        public Guid? SupplierId { get; set; }
        [MaxLength(120)] public string? Reference { get; set; }
        [MaxLength(2000)] public string? Notes { get; set; }
        public DateTime? ExpectedAt { get; set; }
    }
}
