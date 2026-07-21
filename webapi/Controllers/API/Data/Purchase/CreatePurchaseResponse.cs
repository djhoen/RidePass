namespace webapi.Controllers.API.Data.Purchase
{
    public class CreatePurchaseResponse
    {
        // First purchase id / token — kept for back-compat with pass single-buy callers.
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        // Cart purchases populate this with one entry per ticket so the rider sees a QR
        // for each. Single-item purchases include the same id/token here too for uniformity.
        public List<TicketRedemption> Tickets { get; set; } = new();
        public string ClientSecret { get; set; } = null!;
        public int AmountCents { get; set; }
        public int RiderServiceChargeCents { get; set; }
        // Admission tax contained in AmountCents (0 when the tenant has no admission tax). For
        // tax-inclusive pricing this is the portion already baked into AmountCents; for on-top
        // pricing it is what was added. Lets the checkout show a tax line.
        public int TaxCents { get; set; }
        // When a gift card was applied, this is the amount drawn from its balance.
        // Stripe is charged AmountCents - GiftCardAppliedCents.
        public int GiftCardAppliedCents { get; set; }
        // Store credit applied as a tender (already deducted from AmountCents).
        public int CreditAppliedCents { get; set; }

        // Set when a bike rental was bundled with a lesson. The rental FEE is already part of
        // AmountCents (the single ticket charge). The refundable deposit is a SEPARATE hold the
        // client confirms after the main charge, with the same card. Null when no bike was added
        // or the bike had no deposit.
        public string? DepositHoldClientSecret { get; set; }
        // The bundled bike's rental fee (already inside AmountCents) and deposit-hold amount, so
        // the checkout can show both line items and explain the hold.
        public int RentalFeeCents { get; set; }
        public int RentalDepositCents { get; set; }
    }

    public class TicketRedemption
    {
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        public string TierName { get; set; } = string.Empty;
        public int AmountCents { get; set; }
    }
}
