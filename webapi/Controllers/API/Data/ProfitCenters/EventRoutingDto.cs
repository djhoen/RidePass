namespace webapi.Controllers.API.Data.ProfitCenters
{
    public class EventRoutingDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        /// <summary>Null = the source-kind default (event ticket revenue).</summary>
        public string? RevenueKey { get; set; }
    }
}
