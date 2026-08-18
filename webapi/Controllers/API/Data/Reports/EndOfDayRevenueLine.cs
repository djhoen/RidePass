namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// One revenue line of the End of Day report. The key is a
    /// Services.Accounting.QboAccountKeys slot, so this line and the corresponding credit on that
    /// day's QuickBooks journal entry are the same number by construction.
    /// </summary>
    public class EndOfDayRevenueLine
    {
        /// <summary>The QBO account slot, e.g. revenue_event_ticket.</summary>
        public string Key { get; set; } = null!;
        public string Label { get; set; } = null!;
        public int SaleCount { get; set; }
        public int RefundCount { get; set; }
        /// <summary>Gross on sale rows only. Tax- and tip-inclusive.</summary>
        public long GrossCents { get; set; }
        /// <summary>Gross on refund rows. Negative.</summary>
        public long RefundCents { get; set; }
        /// <summary>GrossCents + RefundCents.</summary>
        public long NetGrossCents { get; set; }
        public long TaxCents { get; set; }
        public long TipCents { get; set; }
        /// <summary>What the track actually earned on this line: NetGross minus the tax and tips it only holds.</summary>
        public long NetRevenueCents { get; set; }
    }
}
