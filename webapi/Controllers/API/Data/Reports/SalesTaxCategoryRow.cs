namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>Sales tax collected on one QuickBooks revenue slot over the period.</summary>
    public class SalesTaxCategoryRow
    {
        /// <summary>The QBO account slot, e.g. revenue_concession.</summary>
        public string Key { get; set; } = null!;
        public string Label { get; set; } = null!;
        /// <summary>Tax on sales minus tax refunded. The remittable figure.</summary>
        public long TaxCents { get; set; }
        public long CollectedTaxCents { get; set; }
        /// <summary>Negative.</summary>
        public long RefundedTaxCents { get; set; }
        /// <summary>Net gross (sales plus refunds) on the rows that carried tax.</summary>
        public long TaxableSalesCents { get; set; }
        public int SaleCount { get; set; }
    }
}
