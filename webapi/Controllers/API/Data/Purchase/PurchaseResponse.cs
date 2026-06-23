namespace webapi.Controllers.API.Data.Purchase
{
    public class PurchaseResponse
    {
        public Guid Id { get; set; }
        // Discriminator from v_recent_sales — 'pass', 'event_ticket',
        // 'event_extra', 'season_pass', 'membership', 'gift_card', 'rental'.
        // Lets the admin UI render kind-specific actions (e.g., which Cancel
        // endpoint to call) and hide columns that only apply to one kind.
        public string Kind { get; set; } = null!;
        public string ProductName { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public int AmountCents { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        // Redemption token (the rider-facing "Order #" source); null for membership/gift card.
        // Lets the admin list show a matching short Order # and search by it.
        public string? RedemptionToken { get; set; }
    }
}
