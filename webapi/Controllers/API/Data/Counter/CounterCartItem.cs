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

        // "Another rider" on the payer's order (an event_ticket gate fee / day pass, quantity 1): who it is
        // for, as entered at the counter. The sale re-checks it and creates or links their profile (and a
        // minor's guardian) when it is made. Null = the payer's own line, as before.
        public CounterOtherRiderInput? OtherRider { get; set; }

        // "rental" only: the customer's height (whole inches) and weight (whole pounds) the bike is sized for
        // (Script0317). Null = use what is on the customer's profile; the sale is refused when neither has them.
        // Sent values are saved to the customer's profile and frozen on the rental.
        public int? RiderHeightIn { get; set; }
        public int? RiderWeightLb { get; set; }
    }
}
