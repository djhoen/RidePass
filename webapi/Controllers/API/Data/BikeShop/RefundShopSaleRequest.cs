using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class RefundShopSaleRequest
    {
        /// <summary>
        /// True when the goods came back and should return to stock. A refund is not always a
        /// return (a price-adjustment refund, a damaged item kept by the customer), so the cashier
        /// decides rather than the system assuming.
        /// </summary>
        public bool Restock { get; set; }

        /// <summary>
        /// Where the money goes: back to the original payment ('original'), or onto the
        /// customer's store credit balance ('credit'). Any credit the sale was paid with is
        /// always restored to its account regardless of this choice.
        /// </summary>
        [RegularExpression("^(original|credit)$")]
        public string Destination { get; set; } = "original";

        [MaxLength(500)] public string? Note { get; set; }
    }
}
