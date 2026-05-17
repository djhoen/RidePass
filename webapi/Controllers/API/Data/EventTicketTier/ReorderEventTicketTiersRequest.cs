using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.EventTicketTier
{
    public class ReorderEventTicketTiersRequest
    {
        [Required, MinLength(1)]
        public List<ReorderEventTicketTierItem> Items { get; set; } = new();
    }

    public class ReorderEventTicketTierItem
    {
        [Required] public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
