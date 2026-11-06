namespace webapi.Controllers.API.Data.Concession
{
    // Opens a tab: a sale that is rung up in rounds and settled once at the end. Name is free text
    // ("Red hat table 4"); it may be left blank only when a card is being held, in which case the
    // tab is auto-named from the card once the reader confirms the hold. Items may be empty (open
    // the tab first, ring the first round later). Discount/ManagerPin apply to this first round.
    public class ConcessionOpenTabRequest
    {
        public string? Name { get; set; }
        // Optional attached customer (member lookup); the member perk discount also snapshots them.
        public string? CustomerName { get; set; }
        public List<ConcessionSaleRequest.SaleLine> Items { get; set; } = new();
        public ConcessionDiscountInput? Discount { get; set; }
        public string? ManagerPin { get; set; }
        // Place a card-present hold (capture_method manual) for the tenant's configured hold amount.
        public bool HoldCard { get; set; }
        // Who is opening the tab when staff sign-in is on (see ConcessionSaleRequest.CashierToken).
        public string? CashierToken { get; set; }
    }
}
