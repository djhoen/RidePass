using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    /// <summary>Shop supply fee on repair bills. Charged on LABOR only: a percentage of an
    /// expensive part would track the part's price rather than the consumables the job burned.</summary>
    public class UpdateShopSupplyFeeRequest
    {
        /// <summary>Basis points of the labor subtotal (500 = 5%). 0 turns the fee off.</summary>
        [Range(0, 5000)] public int Bps { get; set; }
        /// <summary>Ceiling in cents; null = uncapped.</summary>
        public int? CapCents { get; set; }
        [MaxLength(60)] public string Label { get; set; } = "Shop supplies";
    }
}
