using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.EventTicketTier
{
    public class UpsertEventTicketTierRequest
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;

        [Range(1, 1_000_000)]
        public int PriceCents { get; set; }

        [Range(1, int.MaxValue)]
        public int? Inventory { get; set; }

        public int SortOrder { get; set; } = 100;

        public bool IsActive { get; set; } = true;
    }
}
