namespace webapi.Controllers.API.Data.Counter
{
    public class CounterSaleResponse
    {
        public string ClientSecret { get; set; } = null!;
        public int TotalAmountCents { get; set; }
        // Store credit applied as a tender and the remainder actually charged/collected.
        public int CreditAppliedCents { get; set; }
        public int DueCents { get; set; }
        // Admission tax contained in TotalAmountCents (0 when the tenant has no admission tax).
        public int TaxCents { get; set; }
        public List<CounterSaleLineItem> LineItems { get; set; } = new();
        // Set only for a card_present (Tap to Pay) sale: the Stripe Terminal Location the
        // mobile SDK scopes reader discovery to. Null for online ('stripe') and cash sales.
        public string? TerminalLocationId { get; set; }
    }

    public class CounterSaleLineItem
    {
        public string Kind { get; set; } = null!;     // "pass" or "event_ticket"
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        public string DisplayName { get; set; } = null!;
        public int Quantity { get; set; }
        public int UnitPriceCents { get; set; }
        public int LineAmountCents { get; set; }
    }
}
