using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class ShopDisplayStateRequest
    {
        // Opaque staff-built snapshot (charges being rung, or a document to read and sign).
        // Relayed to the paired display; never trusted for money or signatures.
        [MaxLength(262144)]
        public string? StateJson { get; set; }
    }
}
