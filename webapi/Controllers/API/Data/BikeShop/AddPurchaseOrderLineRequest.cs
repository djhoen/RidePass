using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class AddPurchaseOrderLineRequest
    {
        [Required] public Guid VariantId { get; set; }
        [Range(1, 100_000)] public int QuantityOrdered { get; set; }
        [Range(0, 100_000_000)] public int UnitCostCents { get; set; }
    }
}
