namespace webapi.Controllers.API.Data.Reports
{
    // Food & Beverage profitability for a date range: revenue, theoretical COGS (from recipes), margin,
    // by item / category / payment method / hour, plus refunds. Paid sales only; refunds shown apart.
    public class ConcessionProfitabilityReport
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }

        public long NetSalesCents { get; set; }     // pre-tax item revenue
        public long TaxCents { get; set; }
        public long TipsCents { get; set; }
        public long GrossSalesCents { get; set; }   // total charged (incl. tax + tip)
        public long CogsCents { get; set; }          // theoretical cost of goods (current recipe cost)
        public long GrossProfitCents { get; set; }   // net sales - COGS
        public double MarginPct { get; set; }        // gross profit / net sales, 0..100
        public int OrderCount { get; set; }
        public long AvgOrderValueCents { get; set; } // gross / order count
        public int RefundedCount { get; set; }
        public long RefundedAmountCents { get; set; }

        public List<ItemRow> Items { get; set; } = new();
        public List<CategoryRow> Categories { get; set; } = new();
        public List<PaymentRow> Payments { get; set; } = new();
        public List<HourRow> Hours { get; set; } = new();

        public class ItemRow
        {
            public string Name { get; set; } = "";
            public int QtySold { get; set; }
            public long RevenueCents { get; set; }
            public long CogsCents { get; set; }
            public long ProfitCents { get; set; }
            public double MarginPct { get; set; }
        }

        public class CategoryRow
        {
            public string Category { get; set; } = "";
            public long RevenueCents { get; set; }
            public long CogsCents { get; set; }
            public long ProfitCents { get; set; }
            public double MarginPct { get; set; }
        }

        public class PaymentRow
        {
            public string Method { get; set; } = "";
            public int Count { get; set; }
            public long AmountCents { get; set; }
        }

        public class HourRow
        {
            public int Hour { get; set; }
            public long RevenueCents { get; set; }
            public int OrderCount { get; set; }
        }
    }
}
