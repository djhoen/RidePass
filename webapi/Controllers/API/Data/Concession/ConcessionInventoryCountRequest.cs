using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // A stock take: counted quantities per inventory item. Records variance vs theoretical and reconciles.
    public class ConcessionInventoryCountRequest
    {
        [MaxLength(500)]
        public string? Note { get; set; }
        public List<Line> Lines { get; set; } = new();

        public class Line
        {
            public Guid InventoryItemId { get; set; }
            public decimal CountedQty { get; set; }
        }
    }
}
