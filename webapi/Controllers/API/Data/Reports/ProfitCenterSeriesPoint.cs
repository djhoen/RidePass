namespace webapi.Controllers.API.Data.Reports
{
    public class ProfitCenterSeriesPoint
    {
        /// <summary>Tenant-local business date, yyyy-MM-dd. Matches a DailyRevenue point's date.</summary>
        public string Date { get; set; } = null!;
        /// <summary>Gross sale revenue for that center on that day; 0 on a day it sold nothing.</summary>
        public long RevenueCents { get; set; }
    }
}
