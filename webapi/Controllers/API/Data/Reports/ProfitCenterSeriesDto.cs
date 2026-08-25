namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// One profit center's daily revenue line on the Sales Summary chart, carrying the color it
    /// wears on every other screen so the chart, the End of Day report and the settings page all
    /// agree about which color means which part of the business.
    /// </summary>
    public class ProfitCenterSeriesDto
    {
        /// <summary>Stable per-entity key ("pc:{guid}" for a tenant's own center, else the
        /// built-in department key). Colors follow this, never the series' rank in the list.</summary>
        public string Key { get; set; } = null!;
        public string Label { get; set; } = null!;
        public string Color { get; set; } = null!;
        /// <summary>The period total, for the table under the chart.</summary>
        public long TotalCents { get; set; }
        /// <summary>Every day in the range, gapless and in the same order as DailyRevenue.</summary>
        public List<ProfitCenterSeriesPoint> Points { get; set; } = new();
    }
}
