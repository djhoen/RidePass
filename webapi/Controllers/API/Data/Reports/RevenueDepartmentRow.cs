namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>One business unit's revenue for the period, with the account slots that make it up.</summary>
    public class RevenueDepartmentRow
    {
        /// <summary>Stable identifier from QboDepartments, e.g. training. Safe to key UI state on.</summary>
        public string Key { get; set; } = null!;
        public string Label { get; set; } = null!;

        /// <summary>Gross minus tax minus tips, net of refunds. The number the report ranks by.</summary>
        public long NetRevenueCents { get; set; }
        public long GrossCents { get; set; }
        public long TaxCents { get; set; }
        public long TipCents { get; set; }
        /// <summary>Negative, and already inside GrossCents.</summary>
        public long RefundCents { get; set; }
        public int SaleCount { get; set; }
        public int RefundCount { get; set; }

        /// <summary>
        /// Share of the period's total net revenue, 0-100, rounded to one decimal. Computed server
        /// side so the report, its CSV and any future consumer agree on the rounding.
        ///
        /// Can exceed 100 or go negative in the edge case where one department is net negative for
        /// the period (refunds outran sales), because the denominator is the signed total. That is
        /// honest: hiding it behind a clamp would make a refund-heavy month look ordinary.
        /// </summary>
        public decimal PctOfTotal { get; set; }

        /// <summary>The QuickBooks revenue slots inside this department, biggest first.</summary>
        public List<RevenueCategoryRow> Categories { get; set; } = new();
    }
}
