using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.DayPricing
{
    /// <summary>A peak day: a weekday or one date, and how much more a day pass costs then.</summary>
    public class CreateDayPriceRuleRequest
    {
        [Required, MaxLength(80)] public string Label { get; set; } = "";
        /// <summary>0 = Sunday .. 6 = Saturday. Give this or OnDate.</summary>
        [Range(0, 6)] public int? Weekday { get; set; }
        public DateOnly? OnDate { get; set; }
        /// <summary>Null = every event type.</summary>
        public Guid? EventTypeId { get; set; }
        /// <summary>'amount' (cents) or 'percent' (basis points, 2500 = +25%).</summary>
        [Required, RegularExpression("^(amount|percent)$")] public string AdjustKind { get; set; } = "amount";
        [Range(1, 1_000_000)] public int AdjustValue { get; set; }
    }
}
