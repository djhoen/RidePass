using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class ReturnShopRentalRequest
    {
        /// <summary>Damage kept out of the deposit, in cents. 0 = clean return (hold released in
        /// full). Clamped server-side to the authorized deposit.</summary>
        [Range(0, 10_000_000)] public int DepositCapturedCents { get; set; }

        [MaxLength(2000)] public string? ConditionNotes { get; set; }
    }
}
