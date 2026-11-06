using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Buy one used listing online. The body is empty in v1; the fields exist only so a client that
    /// sends a coupon, gift card or store credit gets a clear "not on a used bike yet" rather than a
    /// silently ignored discount.
    /// </summary>
    public class UsedListingOrderRequest
    {
        [MaxLength(40)] public string? CouponCode { get; set; }
        [MaxLength(40)] public string? GiftCardCode { get; set; }
        [Range(0, 5_000_000)] public int CreditCents { get; set; }
    }
}
