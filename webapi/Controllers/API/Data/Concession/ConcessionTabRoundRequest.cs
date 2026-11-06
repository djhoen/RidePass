namespace webapi.Controllers.API.Data.Concession
{
    // Another round on an open tab. Lines resolve exactly like a counter sale; Discount/ManagerPin
    // apply to this round's lines only (an order-level discount at close spreads across every round).
    public class ConcessionTabRoundRequest
    {
        public List<ConcessionSaleRequest.SaleLine> Items { get; set; } = new();
        public ConcessionDiscountInput? Discount { get; set; }
        public string? ManagerPin { get; set; }
    }
}
