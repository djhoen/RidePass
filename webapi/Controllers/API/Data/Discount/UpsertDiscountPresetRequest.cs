using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Discount
{
    /// <summary>Create or update a tenant discount. Value is basis points when Kind is 'percent'
    /// (1000 = 10%) and cents when 'amount'; the controller range-checks per kind, since the two
    /// have nothing in common numerically.</summary>
    public class UpsertDiscountPresetRequest
    {
        [Required, MaxLength(60)]
        public string Name { get; set; } = null!;

        [Required, RegularExpression("^(percent|amount)$", ErrorMessage = "Kind must be 'percent' or 'amount'.")]
        public string Kind { get; set; } = "percent";

        [Range(1, 10_000_000)]
        public int Value { get; set; }

        /// <summary>Which counters may apply it. At least one; validated against the known set.</summary>
        [Required, MinLength(1, ErrorMessage = "Choose at least one place this discount applies.")]
        public List<string> Surfaces { get; set; } = new();

        public bool RequiresManager { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
    }
}
