namespace webapi.Controllers.API.Data.Discount
{
    public class DiscountPresetResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string Kind { get; set; } = null!;
        /// <summary>Basis points when Kind is 'percent', cents when 'amount'.</summary>
        public int Value { get; set; }
        public string[] Surfaces { get; set; } = Array.Empty<string>();
        public bool RequiresManager { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        /// <summary>Ready-to-render form of the value ("10% off", "$2.00 off"), so every counter
        /// and the settings list label it the same way instead of each formatting bps by hand.</summary>
        public string Label { get; set; } = null!;
    }
}
