namespace webapi.Controllers.API.Data.Concession
{
    // Void/comp report payload: the comped F&B sales in a date window plus totals. Structured so refunds
    // / voids can fold in as additional rows/kinds later.
    public class ConcessionCompReportResponse
    {
        public List<ConcessionCompReportRow> Rows { get; set; } = new();
        public int TotalCompCents { get; set; }
        public int Count { get; set; }
    }

    public class ConcessionCompReportRow
    {
        public Guid SaleId { get; set; }
        public int? OrderNumber { get; set; }
        public DateTime CreatedAt { get; set; }
        public int DiscountCents { get; set; }       // amount comped
        public int TotalCents { get; set; }          // what the customer ultimately paid
        public string? CompReasonLabel { get; set; }
        public string? CashierName { get; set; }     // who rang it
        public string? AuthorizedByName { get; set; } // manager who approved the comp
    }
}
