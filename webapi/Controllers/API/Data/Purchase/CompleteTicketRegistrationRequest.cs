using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Purchase
{
    // Post-payment registration for the unified event checkout. The buyer defines one
    // entry per REGISTRANT (a person): their identity + a signed waiver when the event
    // requires one, plus the set of paid tickets that person covers — their rider gate
    // fee and the race classes assigned to them (one rider may hold several classes), or
    // a single spectator gate fee. Ticket ids come from the checkout / resume response.
    public class CompleteTicketRegistrationRequest
    {
        [Required, MinLength(1)]
        public List<RegistrantRegistrationItem> Registrants { get; set; } = new();
    }

    public class RegistrantRegistrationItem
    {
        [MaxLength(120)] public string? FirstName { get; set; }
        [MaxLength(120)] public string? LastName { get; set; }
        public DateTime? Birthdate { get; set; }
        [MaxLength(100)] public string? Bike { get; set; }
        // Parent/guardian name when the registrant is a minor.
        [MaxLength(120)] public string? ParentGuardianName { get; set; }
        // Data-URL of the drawn signature; required when any of this registrant's tickets
        // belong to an audience the event requires a waiver for (rider vs spectator).
        public string? WaiverSignatureDataUrl { get; set; }
        // Every ticket this registrant covers: their gate fee + assigned race classes, or
        // a single spectator gate fee. Each class entry may carry its own race number.
        [Required, MinLength(1)]
        public List<RegistrantTicketItem> Tickets { get; set; } = new();
    }

    public class RegistrantTicketItem
    {
        [Required] public Guid TicketId { get; set; }
        [MaxLength(16)] public string? RaceNumber { get; set; }
    }
}
