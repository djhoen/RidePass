using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Counter
{
    public class CounterCartItem
    {
        // "pass" | "event_ticket" | "extras" | "membership"
        [Required]
        public string Kind { get; set; } = null!;

        // Per-kind:
        //   pass         → PassProduct id
        //   event_ticket → EventTicketTier id
        //   extras       → EventExtraProduct id
        //   membership   → ignored (single tenant-config product); pass any guid
        [Required]
        public Guid ItemId { get; set; }

        [Range(1, 100)]
        public int Quantity { get; set; } = 1;

        // Optional for "extras" — counter sells add-ons as merch; not required.
        public Guid? EventId { get; set; }

        // Optional for "extras" when the product has variants. Required if the
        // product has any active variants (server enforces).
        public Guid? VariantId { get; set; }
    }
}
