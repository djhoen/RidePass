using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Ring up a retail sale at the register. The server recomputes every price and tax from the
    /// catalog — the client sends only what's in the cart, never amounts.
    /// </summary>
    public class RingUpShopSaleRequest
    {
        [Required, MinLength(1)] public List<RingUpLine> Lines { get; set; } = new();

        [Required, RegularExpression("^(cash|card)$")]
        public string PaymentMethod { get; set; } = "card";

        // Optional walk-in identity; an app user can be attached when known. When the email (or
        // user id) resolves to a rider holding a season pass with a retail benefit, the discount
        // applies automatically.
        public Guid? BuyerUserId { get; set; }
        [MaxLength(160)] public string? BuyerName { get; set; }
        [MaxLength(200)] public string? BuyerEmail { get; set; }

        [MaxLength(40)] public string? CouponCode { get; set; }
        [Range(0, 1_000_000)] public int TipCents { get; set; }

        // Store credit as a tender: the account the cashier looked up and how much of its
        // balance to apply. The server re-verifies the balance and caps at the sale total.
        public Guid? CreditAccountId { get; set; }
        [Range(0, 5_000_000)] public int CreditCents { get; set; }

        // Gift card tender: applied after discounts, before store credit.
        [MaxLength(40)] public string? GiftCardCode { get; set; }

        // Card-present: collect the remainder on the WisePOS E reader instead of the on-screen
        // Payment Element. Only meaningful with PaymentMethod 'card'.
        public bool CardPresent { get; set; }
    }

    public class RingUpLine
    {
        [Required] public Guid VariantId { get; set; }
        [Range(1, 1000)] public int Quantity { get; set; } = 1;
        // Required for a serialized variant: which specific unit (bike) is being sold.
        public Guid? ItemId { get; set; }
    }
}
