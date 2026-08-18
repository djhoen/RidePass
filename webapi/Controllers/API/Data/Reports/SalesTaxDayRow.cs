namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>Sales tax collected on one tenant-local business date.</summary>
    public class SalesTaxDayRow
    {
        /// <summary>Tenant-local calendar date, yyyy-MM-dd. A date-only value: do not treat it as a UTC instant.</summary>
        public string BusinessDate { get; set; } = null!;
        /// <summary>Tax on sales minus tax refunded.</summary>
        public long TaxCents { get; set; }
        public long CollectedTaxCents { get; set; }
        /// <summary>Negative.</summary>
        public long RefundedTaxCents { get; set; }
        public long TaxableSalesCents { get; set; }
        public int SaleCount { get; set; }
    }
}
