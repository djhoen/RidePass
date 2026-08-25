namespace webapi.Controllers.API.Data.ProfitCenters
{
    public class SetEventRoutingRequest
    {
        /// <summary>Null = back to the source-kind default (event ticket revenue).</summary>
        public string? RevenueKey { get; set; }
    }
}
