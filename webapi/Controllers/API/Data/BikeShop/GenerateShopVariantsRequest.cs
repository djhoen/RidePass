using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    // Size x color matrix for one product ("enter Team Jersey once, generate S-XL in two
    // colors"). Empty colors means one variant per size (and vice versa).
    public class GenerateShopVariantsRequest
    {
        public List<string> Sizes { get; set; } = new();
        public List<string> Colors { get; set; } = new();
        [MaxLength(30)] public string? SkuPrefix { get; set; }
        [Range(0, 100_000_000)] public int? SalePriceCents { get; set; }
        [Range(0, 100_000_000)] public int? CostCents { get; set; }
        [Range(0, 10_000_000)] public int DepositCents { get; set; }
        [Range(1, 100_000)] public int? LowStockThreshold { get; set; }
    }
}
