using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Book a rental online as the signed-in rider. Identity comes from the token, not the body.
    /// The server re-prices everything from the catalog, auto-assigns free serialized units, and
    /// always takes the refundable deposit as a card hold (online rentals are card-only). The
    /// window is half-open [StartsAt, EndsAt).
    /// </summary>
    public class RentalCustomerBookRequest
    {
        [Required, MinLength(1)] public List<RentalCustomerBookLine> Lines { get; set; } = new();
        [Required] public DateTime StartsAt { get; set; }
        [Required] public DateTime EndsAt { get; set; }
    }

    public class RentalCustomerBookLine
    {
        [Required] public Guid VariantId { get; set; }
        [Range(1, 20)] public int Quantity { get; set; } = 1;
    }
}
