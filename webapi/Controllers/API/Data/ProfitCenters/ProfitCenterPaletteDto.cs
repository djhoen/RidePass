namespace webapi.Controllers.API.Data.ProfitCenters
{
    /// <summary>
    /// The suggested swatches the color picker offers. Served from the backend rather than
    /// duplicated in the Vue app so the recommended colors, the defaults new centers get, and the
    /// colors reports draw are all the same validated list.
    /// </summary>
    public class ProfitCenterPaletteDto
    {
        /// <summary>Recommended center colors, in assignment order.</summary>
        public List<string> Swatches { get; set; } = new();
        /// <summary>Reserved for the all-revenue series; shown as taken so nobody picks it.</summary>
        public string TotalSeriesColor { get; set; } = null!;
    }
}
