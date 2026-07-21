using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Receive stock against a PO line. For a POOL variant, only <see cref="Quantity"/> matters. For
    /// a SERIALIZED variant, supply one <see cref="SerialUnits"/> entry per unit (its count must
    /// equal Quantity) so each physical bike is minted as a tracked item.
    /// </summary>
    public class ReceivePurchaseOrderLineRequest
    {
        [Range(1, 100_000)] public int Quantity { get; set; }
        public List<ReceiveSerialUnit>? SerialUnits { get; set; }
    }

    public class ReceiveSerialUnit
    {
        [Required, MaxLength(160)] public string Label { get; set; } = null!;
        [MaxLength(120)] public string? Serial { get; set; }
    }
}
