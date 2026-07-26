using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class BillShopWorkOrderRequest
    {
        [Required, RegularExpression("^(cash|card)$")]
        public string PaymentMethod { get; set; } = "card";

        // Staff-applied discount from the tenant list (Settings > Discounts), scoped to the
        // 'shop_sale' surface, since a repair bills out as a shop sale.
        public Guid? DiscountPresetId { get; set; }
        // Manager PIN, when the chosen discount is one the tenant marked as needing one. The
        // server decides whether it was required, never the client.
        public string? ManagerPin { get; set; }

        // Required when the paid deposit exceeds the bill: refund the overage (back to the
        // card, or handed from the drawer for a cash deposit) or keep it as store credit.
        [RegularExpression("^(refund|credit)$")]
        public string? ExcessAction { get; set; }
    }
}
