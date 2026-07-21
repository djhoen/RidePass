using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>One line of a season pass order: N passes of a given product.</summary>
    public class SeasonPassCartItem
    {
        [Required] public Guid ProductId { get; set; }

        // Upper bound is a sanity guard, not a business rule — a family buying passes together
        // is the point of the cart, but a four-digit quantity is a fat finger or an attack.
        [Range(1, 20)] public int Quantity { get; set; } = 1;
    }
}
