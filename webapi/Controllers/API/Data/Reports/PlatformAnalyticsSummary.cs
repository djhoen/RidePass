namespace webapi.Controllers.API.Data.Reports
{
    public class PlatformAnalyticsSummary
    {
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
        public long TotalRevenueCents { get; set; }
        public int PassesSold { get; set; }
        public int TicketsSold { get; set; }
        public int RefundedCount { get; set; }
        public int DisputedCount { get; set; }
        public int TotalTenants { get; set; }
        public int ActiveTenants { get; set; }
        public List<DailyRevenuePointDto> DailyRevenue { get; set; } = new();
        public List<TenantBreakdownDto> TenantBreakdown { get; set; } = new();
    }

    public class TenantBreakdownDto
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
