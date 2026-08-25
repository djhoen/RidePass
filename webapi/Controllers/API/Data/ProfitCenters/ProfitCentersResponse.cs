namespace webapi.Controllers.API.Data.ProfitCenters
{
    /// <summary>Everything the Profit Centers settings page renders, in one load.</summary>
    public class ProfitCentersResponse
    {
        /// <summary>True while the tenant has no centers of their own (built-in grouping in force).</summary>
        public bool UsingDefaults { get; set; }
        public List<ProfitCenterDto> Centers { get; set; } = new();
        /// <summary>Every revenue stream the platform can distinguish, assignable or not.</summary>
        public List<RevenueStreamDto> Streams { get; set; } = new();
        /// <summary>Event types with their current revenue routing (Script0274).</summary>
        public List<EventRoutingDto> EventTypes { get; set; } = new();
        /// <summary>The revenue keys an event type may route to (limited by the DB CHECK).</summary>
        public List<RevenueStreamDto> EventRoutingOptions { get; set; } = new();
        /// <summary>Suggested swatches for the color picker.</summary>
        public ProfitCenterPaletteDto Palette { get; set; } = new();
    }
}
