namespace webapi.Controllers.API.Data.Reports
{
    // Food & Beverage sales by employee for a date range: per-seller totals, tender split, tips, and how
    // many of their sales were later refunded. For staff accountability and tip attribution.
    public class ConcessionEmployeeReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public List<Row> Rows { get; set; } = new();

        public class Row
        {
            public Guid? UserId { get; set; }
            public string Name { get; set; } = "";   // empty when unattributed
            public int OrdersCount { get; set; }
            public long GrossSalesCents { get; set; }
            public long NetSalesCents { get; set; }
            public long TaxCents { get; set; }
            public long TipCents { get; set; }
            public long CashCents { get; set; }
            public long CardCents { get; set; }
            public int RefundedCount { get; set; }
            public long RefundedCents { get; set; }
            public long AvgOrderValueCents { get; set; }
        }
    }
}
