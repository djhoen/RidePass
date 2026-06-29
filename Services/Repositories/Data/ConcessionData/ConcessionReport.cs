namespace Services.Repositories.Data.ConcessionData
{
    // ── F&B profitability reporting rows (read models, paid sales in a date range) ────────────────

    // Headline totals for paid concession sales in the window. CogsCents is filled by a separate
    // recipe-cost query and combined by the controller (the joins have different cardinality).
    public class ConcessionSalesAggregate
    {
        public int OrderCount { get; set; }
        public long NetSalesCents { get; set; }   // pre-tax item revenue (subtotal less any included tax)
        public long TaxCents { get; set; }
        public long TipCents { get; set; }
        public long TotalCents { get; set; }       // gross charged (incl. tax + tip)
        public long CogsCents { get; set; }
    }

    public class ConcessionRefundAggregate
    {
        public int RefundedCount { get; set; }
        public long RefundedAmountCents { get; set; }
    }

    public class ConcessionPaymentRow
    {
        public string PaymentMethod { get; set; } = "";
        public int SaleCount { get; set; }
        public long AmountCents { get; set; }
    }

    // Per-item revenue + theoretical COGS (current recipe cost). Combo component lines contribute their
    // cost with no separate revenue (the combo's revenue sits on the entree line).
    public class ConcessionItemProfit
    {
        public string Name { get; set; } = "";
        public int QtySold { get; set; }
        public long RevenueCents { get; set; }
        public long CogsCents { get; set; }
    }

    public class ConcessionCategoryProfit
    {
        public string Category { get; set; } = "";
        public long RevenueCents { get; set; }
        public long CogsCents { get; set; }
    }

    // Net sales bucketed by hour-of-day (0-23) in the tenant timezone, for the daypart chart.
    public class ConcessionHourRow
    {
        public int Hour { get; set; }
        public long RevenueCents { get; set; }
        public int OrderCount { get; set; }
    }

    // Per-employee F&B sales for the staff accountability report. Grouped by the seller
    // (sold_by_user_id); UserId is null for unattributed sales. Refunds are the seller's sales that
    // were later refunded (not who processed the refund).
    public class ConcessionEmployeeSalesRow
    {
        public Guid? UserId { get; set; }
        public string Name { get; set; } = "";
        public int OrdersCount { get; set; }
        public long GrossSalesCents { get; set; }   // total charged incl. tax + tip
        public long NetSalesCents { get; set; }     // pre-tax item revenue
        public long TaxCents { get; set; }
        public long TipCents { get; set; }
        public long CashCents { get; set; }
        public long CardCents { get; set; }
        public int RefundedCount { get; set; }
        public long RefundedCents { get; set; }
    }
}
