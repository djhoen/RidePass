using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class CreateStockCountRequest
    {
        [MaxLength(500)] public string? Notes { get; set; }
    }

    public class SetStockCountLineRequest
    {
        // Null clears the entry back to "not counted".
        [Range(0, 1_000_000)] public int? CountedQty { get; set; }
    }

    /// <summary>Send a receipt for a shop sale by email or text (concessions pattern).</summary>
    public class ShopReceiptRequest
    {
        [Required, MaxLength(200)] public string Destination { get; set; } = null!;
        [RegularExpression("^(email|sms)$")] public string Channel { get; set; } = "email";
    }
}
