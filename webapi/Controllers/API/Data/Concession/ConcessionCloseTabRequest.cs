namespace webapi.Controllers.API.Data.Concession
{
    // Settles an open tab. PaymentMethod: 'cash' (or anything fully covered by credit/comp) pays
    // now; 'card' provisions a NEW card-present PaymentIntent for the reader; 'hold' captures the
    // card held when the tab was opened. Discount is order-level, spread across every round's lines
    // by the same allocation a counter sale uses; ManagerPin authorizes it when the kind needs one.
    public class ConcessionCloseTabRequest
    {
        public string PaymentMethod { get; set; } = "cash";
        public int TipCents { get; set; }
        public Guid? CreditAccountId { get; set; }
        public int CreditCents { get; set; }
        public ConcessionDiscountInput? Discount { get; set; }
        public string? ManagerPin { get; set; }
    }
}
