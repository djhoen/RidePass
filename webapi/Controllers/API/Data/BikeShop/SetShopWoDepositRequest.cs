using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class SetShopWoDepositRequest
    {
        [Range(0, 5_000_000)] public int DepositCents { get; set; }
    }
}
