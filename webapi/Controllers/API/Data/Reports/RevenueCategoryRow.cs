namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// One QuickBooks revenue slot's contribution to its department. The same slot the journal
    /// entry credits, so a category line here is a journal line there.
    /// </summary>
    public class RevenueCategoryRow
    {
        /// <summary>The QBO account slot, e.g. revenue_training.</summary>
        public string Key { get; set; } = null!;
        public string Label { get; set; } = null!;
        /// <summary>Gross minus tax minus tips, net of refunds.</summary>
        public long NetRevenueCents { get; set; }
        public long GrossCents { get; set; }
        public long TaxCents { get; set; }
        public long TipCents { get; set; }
        /// <summary>Negative, and already inside GrossCents.</summary>
        public long RefundCents { get; set; }
        public int SaleCount { get; set; }
        public int RefundCount { get; set; }
    }
}
