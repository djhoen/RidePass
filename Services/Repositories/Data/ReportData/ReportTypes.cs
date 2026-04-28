namespace Services.Repositories.Data.ReportData
{
    public class SalesTotals
    {
        public long RevenueCents { get; set; }
        public int SoldCount { get; set; }
        public int RefundedCount { get; set; }
        public int CancelledCount { get; set; }
        public long RefundedCents { get; set; }
    }

    public class DailyRevenuePoint
    {
        public string Date { get; set; } = null!;
        public long RevenueCents { get; set; }
        public int PassesSold { get; set; }
        public int TicketsSold { get; set; }
    }

    public class TopDayPassProductRow
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = null!;
        public int SoldCount { get; set; }
        public long RevenueCents { get; set; }
    }

    public class TopEventRow
    {
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartUtc { get; set; }
        public int SoldCount { get; set; }
        public long RevenueCents { get; set; }
    }

    public class PlatformSalesTotals
    {
        public long RevenueCents { get; set; }
        public int PassesSold { get; set; }
        public int TicketsSold { get; set; }
        public int RefundedCount { get; set; }
        public int DisputedCount { get; set; }
        public int TotalTenants { get; set; }
        public int ActiveTenants { get; set; }
    }

    public class TenantBreakdownRow
    {
        public Guid TenantId { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public int PassesSold { get; set; }
        public int TicketsSold { get; set; }
        public long RevenueCents { get; set; }
        public int RefundedCount { get; set; }
        public int DisputedCount { get; set; }
    }
}
