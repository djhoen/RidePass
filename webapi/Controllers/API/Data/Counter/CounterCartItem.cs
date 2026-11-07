using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Counter
{
    public class CounterCartItem
    {
        // "event_ticket" | "extras" | "rental" | "membership" | "season_pass"
        // (A gate fee or day pass is sold as an event_ticket tier — there is no separate "pass" kind.)
        [Required]
        public string Kind { get; set; } = null!;

        // Per-kind:
        //   event_ticket → EventTicketTier id
        //   extras       → EventExtraProduct id
        //   rental       → bike shop variant id; EventId is the lesson it attaches to
        //   membership   → ignored (single tenant-config product); pass any guid
        //   season_pass  → SeasonPassProduct id
        [Required]
        public Guid ItemId { get; set; }

        [Range(1, 100)]
        public int Quantity { get; set; } = 1;

        // Optional for "extras" — counter sells add-ons as merch; not required.
        public Guid? EventId { get; set; }

        // Optional for "extras" when the product has variants. Required if the
        // product has any active variants (server enforces).
        public Guid? VariantId { get; set; }

        // "Another rider" on the payer's order (event_ticket gate fee / day pass, quantity 1): the rider's
        // profile from POST Counter/OtherRiders. An adult (or a minor with their own account) has
        // RiderUserId; a minor always has RiderDependentId (their link to a guardian). Both null = the
        // payer's own line, as before.
        public Guid? RiderUserId { get; set; }
        public Guid? RiderDependentId { get; set; }
    }
}
