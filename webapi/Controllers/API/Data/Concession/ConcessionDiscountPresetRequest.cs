using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // Create/update a tenant discount preset the POS shows as a one-tap button.
    public class ConcessionDiscountPresetRequest
    {
        [Required, MaxLength(60)] public string Name { get; set; } = null!;
        // 'percent' (Value = basis points, 0..10000) or 'amount' (Value = cents).
        [Required] public string Kind { get; set; } = "percent";
        [Range(0, 10_000_000)] public int Value { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
    }
}
