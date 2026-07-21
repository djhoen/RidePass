using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    // A rider's order from the public shop page: pay online, pick up in store. The server
    // prices everything from the catalog; the client never sends amounts.
    public class ShopOnlineOrderRequest
    {
        [Required, MinLength(1)] public List<ShopOnlineOrderLine> Lines { get; set; } = new();
        [MaxLength(40)] public string? CouponCode { get; set; }
        // Burn my store credit (server resolves the account by the signed-in user and caps).
        [Range(0, 5_000_000)] public int CreditCents { get; set; }
    }

    public class ShopOnlineOrderLine
    {
        [Required] public Guid VariantId { get; set; }
        [Range(1, 100)] public int Quantity { get; set; } = 1;
    }
}
