using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class UpsertShopVariantRequest
    {
        [MaxLength(80)] public string? Sku { get; set; }
        [MaxLength(80)] public string? Barcode { get; set; }
        [MaxLength(60)] public string? Size { get; set; }
        [MaxLength(60)] public string? Color { get; set; }
        [MaxLength(40)] public string? Gender { get; set; }

        [Range(0, 100_000_000)] public int? SalePriceCents { get; set; }
        [Range(0, 100_000_000)] public int? MsrpCents { get; set; }
        [Range(0, 100_000_000)] public int? DailyRateCents { get; set; }
        [Range(0, 100_000_000)] public int DepositCents { get; set; }
        [Range(0, 100_000_000)] public int? CostCents { get; set; }
        [MaxLength(80)] public string? Mpn { get; set; }

        /// <summary>
        /// The manufacturer's own name for this part. Distinct from the product's Name, which is
        /// what THIS shop calls it and stays private to this tenant. This is the only field that
        /// feeds the cross-tenant parts library, so it must be the manufacturer's wording and not
        /// the shop's; leaving it blank simply means the part is never contributed.
        /// </summary>
        [MaxLength(200)] public string? ManufacturerName { get; set; }

        // Only meaningful on create; a variant's tracking kind is fixed once stock exists against it.
        [RegularExpression("^(pool|serialized)$")]
        public string TrackingKind { get; set; } = "pool";

        // Alert managers when on-hand falls to/below this. Null = no alerting.
        [Range(0, 100_000)] public int? LowStockThreshold { get; set; }

        // Reorder planning (pool). Point triggers the reorder list; level sizes the suggested order.
        [Range(0, 100_000)] public int? ReorderPoint { get; set; }
        [Range(0, 100_000)] public int? ReorderLevel { get; set; }
        [MaxLength(80)] public string? VendorPartNumber { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
