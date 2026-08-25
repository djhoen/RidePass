namespace webapi.Controllers.API.Data.ProfitCenters
{
    public class RevenueStreamDto
    {
        /// <summary>The QboAccountKeys revenue slot, e.g. revenue_bike_shop.</summary>
        public string Key { get; set; } = null!;
        public string Label { get; set; } = null!;
        /// <summary>Where this stream lands when unassigned: its built-in department's label.</summary>
        public string DefaultCenterLabel { get; set; } = null!;
    }
}
