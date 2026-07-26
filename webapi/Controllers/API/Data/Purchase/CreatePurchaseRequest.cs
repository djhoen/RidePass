using System.ComponentModel.DataAnnotations;
using webapi.Controllers.API.Data.Extras;

namespace webapi.Controllers.API.Data.Purchase
{
    public class CreatePurchaseRequest
    {
        [Required]
        public Guid ProductId { get; set; }

        public DateTime? ValidOnDate { get; set; }

        // For reservation-bound purchases (tenant.require_reservation_for_passes = true).
        public Guid? EventId { get; set; }

        [Range(1, 50)]
        public int Quantity { get; set; } = 1;


        [MaxLength(40)]
        public string? CouponCode { get; set; }

        [MaxLength(40)]
        public string? GiftCardCode { get; set; }

        // Optional event extras (camping/parking/pit-vehicle/...) bundled with the pass.
        // Same Stripe PI covers both — webhook flips both rows to paid together.
        public List<BuyExtrasItem>? Extras { get; set; }

        // When true and this rider doesn't already have an active membership, a
        // membership purchase row is created and bundled into the same PaymentIntent
        // — the alternative to redirecting them through the standalone /Membership flow.
        public bool AddMembership { get; set; }
    }
}
