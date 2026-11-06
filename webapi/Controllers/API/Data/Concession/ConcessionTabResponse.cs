namespace webapi.Controllers.API.Data.Concession
{
    // Result of opening a tab. HoldClientSecret is set only when a card hold was requested: the POS
    // runs the Terminal collect + process flow on it, then calls Tab/{id}/HoldConfirmed.
    public class ConcessionTabResponse
    {
        public Guid SaleId { get; set; }
        public int? OrderNumber { get; set; }
        public string? TabName { get; set; }
        public int TotalCents { get; set; }
        public int Rounds { get; set; }
        public string? HoldClientSecret { get; set; }
        public string? HoldPaymentIntentId { get; set; }
    }
}
