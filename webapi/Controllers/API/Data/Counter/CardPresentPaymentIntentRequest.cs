using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>
    /// Mobile cashier app's request to create a card-present PaymentIntent for
    /// the Stripe Terminal SDK to collect via tap-to-pay. The server provisions
    /// the PI; the SDK on the device collects the payment method, confirms, and
    /// the webhook handler later flips the corresponding sale rows to paid.
    ///
    /// The rider + cart shape mirrors CounterSaleRequest so the same validation
    /// (membership gate, waiver gate, inventory checks, voucher rules) applies.
    /// </summary>
    public class CardPresentPaymentIntentRequest
    {
        [Required] public Guid RiderId { get; set; }
        [Required, MinLength(1)] public List<CounterCartItem> Items { get; set; } = new();
        public Guid? RewardRedemptionId { get; set; }
        [MaxLength(40)] public string? CouponCode { get; set; }
        [MaxLength(40)] public string? GiftCardCode { get; set; }
    }

    public class CardPresentPaymentIntentResponse
    {
        public string PaymentIntentId { get; set; } = null!;
        public string ClientSecret { get; set; } = null!;
        public long AmountCents { get; set; }
        // Whether the cart requires a waiver signature before the PI can be
        // confirmed (the mobile app gates the tap-to-pay step on this).
        public bool RequiresWaiverSignature { get; set; }
        public string? RequiredWaiverId { get; set; }
    }
}
