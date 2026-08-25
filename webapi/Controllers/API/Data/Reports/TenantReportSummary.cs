namespace webapi.Controllers.API.Data.Reports
{
    public class TenantReportSummary
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public long TotalRevenueCents { get; set; }
        public int PassesSold { get; set; }
        public int TicketsSold { get; set; }
        public int UniqueRiders { get; set; }
        public int RefundedCount { get; set; }
        public int CancelledCount { get; set; }
        public int DisputedCount { get; set; }
        public long RefundedAmountCents { get; set; }
        // Gross revenue split by type (event_ticket, season_pass, membership, extras, rental,
        // concession, ...). Sums to TotalRevenueCents. Lets the UI break the headline out by kind.
        public List<RevenueByKindDto> RevenueByType { get; set; } = new();
        public List<DailyRevenuePointDto> DailyRevenue { get; set; } = new();
        /// <summary>
        /// The same daily money as DailyRevenue, split by profit center: the chart draws the total
        /// in the reserved blue and each center in its own color. Series sum to DailyRevenue day by
        /// day. Empty only when the range has no revenue at all.
        /// </summary>
        public List<ProfitCenterSeriesDto> RevenueByProfitCenter { get; set; } = new();
        public List<TopProductDto> TopPassProducts { get; set; } = new();
        public List<TopEventDto> TopEvents { get; set; } = new();
    }

    public class RevenueByKindDto
    {
        public string Kind { get; set; } = null!;
        public long RevenueCents { get; set; }
        public int SaleCount { get; set; }
    }

    public class DailyRevenuePointDto
    {
        public string Date { get; set; } = null!;
        public long RevenueCents { get; set; }
        public int PassesSold { get; set; }
        public int TicketsSold { get; set; }
    }

    public class TopProductDto
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = null!;
        public int SoldCount { get; set; }
        public long RevenueCents { get; set; }
    }

    public class TopEventDto
    {
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartUtc { get; set; }
        public int SoldCount { get; set; }
        public long RevenueCents { get; set; }
    }
}
