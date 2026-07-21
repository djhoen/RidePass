using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class BillShopWorkOrderRequest
    {
        [Required, RegularExpression("^(cash|card)$")]
        public string PaymentMethod { get; set; } = "card";

        [Range(0, 1_000_000)] public int TipCents { get; set; }

        // Required when the paid deposit exceeds the bill: refund the overage (back to the
        // card, or handed from the drawer for a cash deposit) or keep it as store credit.
        [RegularExpression("^(refund|credit)$")]
        public string? ExcessAction { get; set; }
    }
}
