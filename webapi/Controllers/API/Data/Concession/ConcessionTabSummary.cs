namespace webapi.Controllers.API.Data.Concession
{
    // One row of the POS "Open tabs" drawer.
    public class ConcessionTabSummary
    {
        public Guid SaleId { get; set; }
        public int? OrderNumber { get; set; }
        public string? TabName { get; set; }
        public string? CustomerName { get; set; }
        public DateTime OpenedAtUtc { get; set; }
        public DateTime? LastRoundAtUtc { get; set; }
        public int Rounds { get; set; }
        public int ItemCount { get; set; }
        public int TotalCents { get; set; }
        // "Visa ••4242" when a card is held on the tab, else null.
        public string? HoldCardLabel { get; set; }
    }
}
