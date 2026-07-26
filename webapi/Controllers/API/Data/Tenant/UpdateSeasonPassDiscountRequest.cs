using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    /// <summary>
    /// The standing discount for season pass holders. Enable, kind, value and the surfaces travel
    /// together because turning it on without a value is a switch that does nothing, and the UI
    /// would otherwise need several calls to avoid a moment where it is on at 0% or on everywhere.
    /// </summary>
    public class UpdateSeasonPassDiscountRequest
    {
        public bool Enabled { get; set; }

        /// <summary>'percent' (basis points) or 'amount' (cents).</summary>
        public string Kind { get; set; } = "percent";

        /// <summary>Basis points for 'percent' (10000 = 100%), cents for 'amount'.</summary>
        [Range(0, 10_000_000)]
        public int Value { get; set; }

        // Where it applies. The amount is shared; the surfaces are chosen, because a percentage
        // picked with a $9 burger in mind is the same percentage off a $6,000 bike.
        public bool AppliesConcession { get; set; } = true;
        public bool AppliesRetail { get; set; } = true;
        public bool AppliesRental { get; set; } = true;
    }
}
