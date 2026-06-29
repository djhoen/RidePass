using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionInventoryItemRequest
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;
        [MaxLength(20)]
        public string Unit { get; set; } = "each";
        [Range(0, int.MaxValue)]
        public int CostCents { get; set; }
        public decimal OnHand { get; set; }
        // Optional low-stock threshold; when on-hand falls to/below it the item flags low. Null = off.
        public decimal? LowStockThreshold { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
