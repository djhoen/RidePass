namespace Services.Repositories.Data.PaymentData
{
    public class EventTicketPurchase
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid TierId { get; set; }
        public Guid? PurchaserUserId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public int AmountCents { get; set; }
        public int ServiceChargeCents { get; set; }
        public Guid? AppliedRewardRedemptionId { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        public string Status { get; set; } = "pending";
        public string PurchaserEmail { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public Guid RedemptionToken { get; set; }
        public string? CancellationReason { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public string? RefundNote { get; set; }
        public DateTime? RedeemedAtUtc { get; set; }
        public Guid? RedeemedByUserId { get; set; }
        public Guid? SoldByUserId { get; set; }
        // Per-event race number assigned by staff at check-in time. NULL falls
        // back to the rider's profile race_number for displays/exports.
        public string? RaceNumber { get; set; }

        // ── Post-payment registration (unified event checkout) ───────────────────
        // Per-ticket rider identity + signed waiver, captured AFTER payment so a guest
        // can buy several entries for riders who aren't accounts. RegistrationComplete
        // flips true once identity + any required waiver are captured; gate check-in
        // flags tickets that are still incomplete.
        public string? RiderFirstName { get; set; }
        public string? RiderLastName { get; set; }
        public DateTime? RiderBirthdate { get; set; }
        public string? Bike { get; set; }
        public Guid? WaiverId { get; set; }
        public DateTime? WaiverSignedAt { get; set; }
        public string? WaiverSignatureDataUrl { get; set; }
        public string? ParentGuardianName { get; set; }
        // Groups a rider's gate fee + their race-class entries within one order so the
        // post-payment step can attach each class to a rider (one rider may hold several
        // classes) and charge exactly one gate fee per rider. NULL = ungrouped (simple
        // single-entry or pre-gate-fee orders).
        public Guid? RegistrantId { get; set; }
        // Defaults true so the existing/at-POS purchase paths (which capture rider +
        // waiver up front) stay "complete"; only the deferred unified-checkout path sets
        // it false and fills registration in afterward.
        public bool RegistrationComplete { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EventTicketPurchaseWithContext : EventTicketPurchase
    {
        public string TierName { get; set; } = null!;
        public string TierKind { get; set; } = null!;   // 'spectator_pass' | 'race_entry'
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public string? EventDescription { get; set; }
        public string? EventLocationLabel { get; set; }
        public DateTime EventStartsAt { get; set; }
        public DateTime EventEndsAt { get; set; }
        public bool EventAllDay { get; set; }
    }

    // Reminder-worker projection: an incomplete paid ticket eligible for a
    // "finish your registration" email, with the bits needed to build + send it.
    public class RegistrationReminderRow
    {
        public Guid TicketId { get; set; }
        public Guid RedemptionToken { get; set; }
        public string? PaymentIntentId { get; set; }
        public Guid TenantId { get; set; }
        public string TenantSubdomain { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public string EventTitle { get; set; } = null!;
    }

    // Resume-page projection: an incomplete ticket in an order, with what the
    // registration form needs (rider vs spectator + whether that audience needs a waiver).
    // Rider-facing "my order for this event" projection (Me/EventOrder). One row per
    // ticket the rider holds for an event — entries + gate fees — with status, the rider
    // it's registered to, and whether registration/waiver is done.
    public class UserEventOrderItem
    {
        public Guid Id { get; set; }
        public string TierName { get; set; } = null!;
        public string Kind { get; set; } = null!;       // race_entry | gate_fee
        public string Audience { get; set; } = "rider";  // rider | spectator
        public string Status { get; set; } = null!;
        public int AmountCents { get; set; }       // what the buyer paid (incl. their service-fee share)
        public int BasePriceCents { get; set; }    // tier list price (pre-fee); AmountCents - this = rider service fee
        public string? RaceNumber { get; set; }
        public string? RiderName { get; set; }
        public bool RegistrationComplete { get; set; }
        public bool WaiverSigned { get; set; }
        public Guid RedemptionToken { get; set; }
        public string EventTitle { get; set; } = null!;
    }

    // Resend-confirmation projection (Me/EventOrder/{id}/ResendConfirmation): the rider's
    // paid/redeemed rows for one event, denormalized with tenant + purchaser + event title
    // so the consolidated confirmation email can be rebuilt without extra lookups.
    public class OrderConfirmationRow
    {
        public Guid TenantId { get; set; }
        public string TenantSubdomain { get; set; } = null!;
        public string TenantDisplayName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public string EventTitle { get; set; } = null!;
        public string TierName { get; set; } = null!;
        public int AmountCents { get; set; }
        public string Status { get; set; } = null!;
        public Guid RedemptionToken { get; set; }
    }

    public class IncompleteRegistrationTicket
    {
        public Guid TicketId { get; set; }
        public string TierName { get; set; } = null!;
        public string Kind { get; set; } = null!;     // race_entry | gate_fee
        public string Audience { get; set; } = "rider"; // rider | spectator (gate_fee)
        public bool Required { get; set; }
        public string EventTitle { get; set; } = null!;
        public bool RequiresRiderWaiver { get; set; }
        public bool RequiresSpectatorWaiver { get; set; }
    }
}
