using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // Create/update a tenant comp reason ("Rider comp", "Employee meal", "Manager comp").
    public class ConcessionCompReasonRequest
    {
        [Required, MaxLength(60)] public string Name { get; set; } = null!;
        // 'full' comps the whole price; 'percent' (DefaultValue = bps) or 'amount' (DefaultValue = cents).
        [Required] public string DefaultKind { get; set; } = "full";
        [Range(0, 10_000_000)] public int DefaultValue { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
    }
}
