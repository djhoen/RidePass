using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Create one purchase order from picked reorder rows. All lines are for the one
    /// supplier named here (or no supplier, which the buyer fills in later).</summary>
    public class CreateReorderPoRequest
    {
        public Guid? SupplierId { get; set; }
        [MaxLength(80)] public string? Reference { get; set; }
        public DateTime? ExpectedAt { get; set; }
        [Required] public List<CreateReorderPoLine> Lines { get; set; } = new();
    }

    public class CreateReorderPoLine
    {
        public Guid VariantId { get; set; }
        [Range(1, 100000)] public int QuantityOrdered { get; set; }
        public int? UnitCostCents { get; set; }
    }
}
