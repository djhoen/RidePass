using System.ComponentModel.DataAnnotations;
using webapi.Controllers.API.Data.Extras;

namespace webapi.Controllers.API.Data.Purchase
{
    public class CreateTicketPurchaseRequest
    {
        // Cart of admission items. Single-tier single-quantity is the common case (one
        // spectator pass, one race entry); supplying multiple tiers / quantities lets a
        // family or group buy in one transaction with a single Stripe charge. Each unit
        // becomes its own event_ticket_purchase row (and its own redemption QR).
        [Required, MinLength(1)]
        public List<TicketCartItem> Items { get; set; } = new();

        // Required for guest checkout; ignored when the request carries a valid JWT.
        [EmailAddress, MaxLength(200)]
        public string? Email { get; set; }

        [MaxLength(120)]
        public string? Name { get; set; }

        // Vouchers only valid when the cart is exactly one tier with quantity = 1.
        public Guid? RewardRedemptionId { get; set; }

        // Optional tenant-issued coupon code (typed by the rider). Stacks with neither
        // reward vouchers nor other coupons — first valid one wins, second is rejected.
        [MaxLength(40)]
        public string? CouponCode { get; set; }

        // Optional gift card code. Treated as a payment method, not a discount —
        // applied AFTER any voucher/coupon, capped at remaining balance, with the
        // leftover charged to Stripe.
        [MaxLength(40)]
        public string? GiftCardCode { get; set; }

        // Burn the signed-in rider's store credit balance as the last tender (server resolves
        // the account and caps at the remaining charge; ignored for guests).
        public bool UseStoreCredit { get; set; }

        // Optional event extras (camping/parking/...) bundled with the tickets.
        // Same Stripe PI covers both. Guest checkout supported.
        public List<BuyExtrasItem>? Extras { get; set; }

        // Optional bike rental added while booking a lesson. The rental FEE rides the same
        // PaymentIntent as the lesson ticket (one charge, two line items); the refundable
        // deposit is a separate manual-capture hold returned as DepositHoldClientSecret. The
        // reservation is time-scoped to the lesson's window. Requires a signed-in rider (a
        // rental needs an owning user), so it's ignored for guest checkout.
        public LessonRentalCartItem? LessonRental { get; set; }

        // When true and this signed-in rider doesn't already have an active membership,
        // a membership purchase row is created and bundled into the same PaymentIntent
        // — the alternative to redirecting them through the standalone /Membership flow.
        public bool AddMembership { get; set; }

        // Unified event-checkout mode: take payment first and collect the waiver +
        // per-rider details afterward (via /Purchase/Ticket/CompleteRegistration), instead
        // of gating the purchase on an up-front signed waiver. Lets guests buy race entries
        // for riders who aren't accounts; tickets are created registration_complete = false.
        public bool DeferRegistration { get; set; }
    }

    public class LessonRentalCartItem
    {
        [Required] public Guid VariantId { get; set; }
        [Range(1, 50)] public int Quantity { get; set; } = 1;
    }

    public class TicketCartItem
    {
        [Required] public Guid TierId { get; set; }
        [Range(1, 50)] public int Quantity { get; set; } = 1;
    }
}
