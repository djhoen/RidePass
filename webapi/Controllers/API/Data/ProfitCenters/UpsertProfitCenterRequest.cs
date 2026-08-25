namespace webapi.Controllers.API.Data.ProfitCenters
{
    public class UpsertProfitCenterRequest
    {
        public string Name { get; set; } = null!;
        /// <summary>
        /// #RRGGBB. Omitted on create means "the next unused palette slot"; omitted on update
        /// keeps the center's current color.
        /// </summary>
        public string? Color { get; set; }
    }
}
