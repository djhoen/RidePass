namespace webapi.Controllers.API.Data.ProfitCenters
{
    public class ProfitCenterDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public int SortOrder { get; set; }
        /// <summary>#RRGGBB this center is drawn in across every screen and chart.</summary>
        public string Color { get; set; } = null!;
        /// <summary>The revenue slots assigned to this center.</summary>
        public List<string> RevenueKeys { get; set; } = new();
    }
}
