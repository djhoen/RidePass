using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionModifierOptionRequest
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;
        // May be negative (a discount option) or positive (an upcharge).
        public int PriceDeltaCents { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
