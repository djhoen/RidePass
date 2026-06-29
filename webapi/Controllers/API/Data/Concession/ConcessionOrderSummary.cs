namespace webapi.Controllers.API.Data.Concession
{
    // A row in the staff order-history list (cashiers + cooks).
    public class ConcessionOrderSummary
    {
        public Guid SaleId { get; set; }
        public int? OrderNumber { get; set; }
        public string Status { get; set; } = "paid";              // paid | refunded
        public string FulfillmentStatus { get; set; } = "active"; // active | ready | completed
        public string PaymentMethod { get; set; } = "stripe";     // stripe | stripe_direct | cash
        public string OrderChannel { get; set; } = "counter";     // counter | online
        public string? CustomerName { get; set; }
        public int SubtotalCents { get; set; }
        public int TipCents { get; set; }
        public int TaxCents { get; set; }
        // Whether SubtotalCents already includes the tax (true) or tax is added on top to reach total.
        public bool PricesIncludeTax { get; set; }
        // Total discount/comp taken off the order (cents), what kind, a display label, and the manager
        // who approved it (for comps / manual discounts). DiscountCents is 0 when nothing was applied.
        public int DiscountCents { get; set; }
        public string? DiscountKind { get; set; }   // 'preset'|'percent'|'amount'|'comp'|'season_pass'|'loampass'|'mixed'
        public string? DiscountLabel { get; set; }
        public string? AuthorizedByName { get; set; }
        public int TotalCents { get; set; }
        public bool IsRush { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? PaidAtUtc { get; set; }
    }
}
