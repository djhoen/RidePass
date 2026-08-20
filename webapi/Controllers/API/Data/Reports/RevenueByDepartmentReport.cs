namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// Earned revenue in a period, rolled up into business units: Tickets &amp; Passes, Training
    /// Center, Food &amp; Beverage, Bike Shop and Other.
    ///
    /// Built from v_accounting_entries and bucketed with QboAccountKeys.EffectiveRevenueKey, the
    /// same call the QuickBooks journal entry and the End of Day report make, so a department total
    /// here is the sum of exactly the journal lines that posted. It is the P&amp;L view of the same
    /// money the End of Day report closes a single day on.
    ///
    /// Only 'sale' and 'refund' rows are in it. A gift card being bought, a deposit being held and
    /// a chargeback are all money moving without being earned, and they belong on the End of Day
    /// report's totals rather than on a department's revenue line.
    /// </summary>
    public class RevenueByDepartmentReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public string Timezone { get; set; } = null!;

        /// <summary>Gross minus tax minus tips, net of refunds. What the departments actually earned.</summary>
        public long NetRevenueCents { get; set; }
        /// <summary>Tax- and tip-inclusive, net of refunds.</summary>
        public long GrossCents { get; set; }
        public long TaxCents { get; set; }
        public long TipCents { get; set; }
        /// <summary>Negative. Included in GrossCents already, broken out so the table can show it.</summary>
        public long RefundCents { get; set; }
        public int SaleCount { get; set; }
        public int RefundCount { get; set; }

        /// <summary>Departments with any activity in the period, in report order. Empty ones are omitted.</summary>
        public List<RevenueDepartmentRow> Departments { get; set; } = new();
    }
}
