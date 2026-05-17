using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Waitlist
{
    public class JoinWaitlistRequest
    {
        [Required] public Guid EventId { get; set; }
        // Required for race / spectator-pass events; null for pass reservation events.
        public Guid? TierId { get; set; }
        // Opt-in pre-pay branch. Only honored when a tierId is supplied (we know
        // exactly what to charge). Day-pass-reservation waitlists can't pre-pay.
        public bool Prepay { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }

    public class JoinWaitlistResponse
    {
        public Guid WaitlistId { get; set; }
        public int Position { get; set; }
        public bool IsPrepaid { get; set; }
        // Set when Prepay=true; rider confirms via Stripe Elements on the page.
        public string? ClientSecret { get; set; }
        public int PrepayAmountCents { get; set; }
        // Echo the phone we'll SMS so the rider can verify before submitting.
        public string? NotifyPhone { get; set; }
    }

    public class MyWaitlistEntryResponse
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartsAtUtc { get; set; }
        public Guid? TierId { get; set; }
        public string? TierName { get; set; }
        public int Position { get; set; }
        public int AheadOfMe { get; set; }
        public bool IsPrepaid { get; set; }
        public int PrepayAmountCents { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? ConfirmDeadlineUtc { get; set; }
        public Guid? ConfirmToken { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class ConfirmDetailsResponse
    {
        public Guid WaitlistId { get; set; }
        public string Status { get; set; } = null!;
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartsAtUtc { get; set; }
        public string? EventLocationLabel { get; set; }
        public Guid? TierId { get; set; }
        public string? TierName { get; set; }
        public int? TierPriceCents { get; set; }
        // Day-pass alternates pick a product at confirm time. Empty for tier-based.
        public List<EligiblePassChoice> EligiblePasses { get; set; } = new();
        public bool IsPrepaid { get; set; }
        public int PrepayAmountCents { get; set; }
        // Promoted-only: when the spot expires.
        public DateTime? ConfirmDeadlineUtc { get; set; }
        // For pre-paid alternates that already auto-confirmed at promotion:
        public Guid? CreatedPurchaseRedemptionToken { get; set; }
    }

    public class EligiblePassChoice
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public int PriceCents { get; set; }
        public bool RequiresWaiver { get; set; }
    }

    public class ConfirmPayRequest
    {
        // Required when the waitlist is event-level (no tier). Must be in the
        // event's eligibility list.
        public Guid? PassProductId { get; set; }
    }

    public class ConfirmPayResponse
    {
        public string ClientSecret { get; set; } = null!;
        public int AmountCents { get; set; }
    }
}
