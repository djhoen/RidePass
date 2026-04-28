namespace webapi.Controllers.API.Data.Dashboard
{
    public class DashboardSnapshot
    {
        public string[] Permissions { get; set; } = Array.Empty<string>();

        // reports.view
        public RevenueBlockDto? TodayRevenue { get; set; }
        public RevenueBlockDto? MonthRevenue { get; set; }
        public int? UniqueRidersMonth { get; set; }
        public List<DailySparkPointDto>? Last7Days { get; set; }

        // no gate — everyone sees upcoming
        public List<UpcomingEventDto> UpcomingEvents { get; set; } = new();

        // sales.view
        public List<RecentPurchaseDto>? RecentPurchases { get; set; }

        // disputes.view / sales.cancel
        public int? OpenDisputesCount { get; set; }
        public int? PendingRefundsCount { get; set; }
    }

    public class RevenueBlockDto
    {
        public long RevenueCents { get; set; }
        public int PassesSold { get; set; }
        public int TicketsSold { get; set; }
    }

    public class DailySparkPointDto
    {
        public string Date { get; set; } = null!;
        public long RevenueCents { get; set; }
    }

    public class UpcomingEventDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public string EventTypeName { get; set; } = null!;
        public string EventTypeColor { get; set; } = null!;
        public int? Capacity { get; set; }
        public string? LocationLabel { get; set; }
    }

    public class RecentPurchaseDto
    {
        public Guid Id { get; set; }
        public string ProductName { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public int AmountCents { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }

    public class DashboardConfigDto
    {
        public List<DashboardWidgetEntry> Widgets { get; set; } = new();
    }

    public class DashboardWidgetEntry
    {
        public string Type { get; set; } = null!;  // matches a frontend widget type key
        public bool Visible { get; set; } = true;
        public int Order { get; set; }
    }
}
