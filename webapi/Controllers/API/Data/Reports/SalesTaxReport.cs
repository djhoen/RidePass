namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// Sales tax collected across EVERY revenue stream in a period, from v_accounting_entries.
    ///
    /// The companion to AdmissionTaxReport, which is deliberately narrower: that one reads
    /// event_ticket_purchase directly and answers "what admission tax do I owe the jurisdiction
    /// that taxes admissions". This one reads the accounting view, so food and beverage, bike
    /// shop, rentals and everything else are in it, broken out the same way the QuickBooks
    /// journal entry breaks them out.
    /// </summary>
    public class SalesTaxReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public string Timezone { get; set; } = null!;

        /// <summary>Tax on sales minus tax refunded, across everything. What the tenant remits.</summary>
        public long NetTaxCents { get; set; }
        public long CollectedTaxCents { get; set; }
        /// <summary>Negative.</summary>
        public long RefundedTaxCents { get; set; }
        /// <summary>Net gross on the rows that carried tax. Tax-inclusive, matching how gross is stored.</summary>
        public long TaxableSalesCents { get; set; }
        public int TaxedSaleCount { get; set; }

        public List<SalesTaxCategoryRow> ByCategory { get; set; } = new();
        public List<SalesTaxDayRow> ByDay { get; set; } = new();
    }
}
