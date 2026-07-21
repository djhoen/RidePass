using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Manual stock correction on a pool variant (initial count, shrinkage, found stock).</summary>
    public class AdjustStockRequest
    {
        // Signed: +N adds, -N removes. Zero is rejected (nothing to record).
        [Required] public int Delta { get; set; }
        [Required, MaxLength(500)] public string Note { get; set; } = null!;
    }
}
