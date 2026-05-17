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
        // When a gift card was applied, this is the amount drawn from its balance.
        // Stripe is charged AmountCents - GiftCardAppliedCents.
        public int GiftCardAppliedCents { get; set; }
    }

    public class TicketRedemption
    {
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        public string TierName { get; set; } = string.Empty;
        public int AmountCents { get; set; }
    }
}
