using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Book a rental at the counter. The server recomputes every rate from the catalog; the window
    /// is half-open [StartsAt, EndsAt). Card bookings return a fee client secret and, when the
    /// gear carries a deposit, a second client secret for the manual-capture deposit hold.
    /// </summary>
    public class BookShopRentalRequest
    {
        [Required, MinLength(1)] public List<BookShopRentalLine> Lines { get; set; } = new();
        [Required] public DateTime StartsAt { get; set; }
        [Required] public DateTime EndsAt { get; set; }

        [Required, RegularExpression("^(cash|card)$")]
        public string PaymentMethod { get; set; } = "card";

        // Card only: also authorize the deposit hold now. Cash bookings record the deposit amount
        // but hold nothing (a cash deposit is a drawer matter, not a Stripe one).
        public bool TakeDepositHold { get; set; } = true;

        // The renter took the damage waiver. Charges a non-refundable fee (a percentage of the
        // gross rental) and waives the deposit entirely. Ignored when the tenant doesn't offer it,
        // so a stale client can't conjure a charge the track never configured.
        public bool Insurance { get; set; }

        // How many riders must each sign the waiver before this gear leaves. Units on a booking
        // are not people (a bike plus a helmet is one rider), so the counter sets it. Null lets the
        // server default it to the largest line quantity, which is right in the common cases.
        [Range(1, 50)] public int? RidersRequired { get; set; }

        public Guid? RenterUserId { get; set; }
        [MaxLength(160)] public string? RenterName { get; set; }
        [MaxLength(200)] public string? RenterEmail { get; set; }
        [MaxLength(40)] public string? RenterPhone { get; set; }
    }

    public class BookShopRentalLine
    {
        [Required] public Guid VariantId { get; set; }
        [Range(1, 100)] public int Quantity { get; set; } = 1;
        // Required for a serialized variant: the specific unit being booked.
        public Guid? ItemId { get; set; }
    }
}
