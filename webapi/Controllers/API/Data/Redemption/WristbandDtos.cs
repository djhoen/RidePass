using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Redemption
{
    public class LinkWristbandRequest
    {
        // Exactly one of these two identifies the wearer. Validated in the controller rather than
        // by attribute: "exactly one of two optional fields" is not expressible with [Required].
        public Guid? TicketId { get; set; }
        public Guid? SeasonPassReservationId { get; set; }
        [Required, MaxLength(200)] public string Code { get; set; } = null!;
    }

    public class UnlinkWristbandRequest
    {
        public Guid? TicketId { get; set; }
        public Guid? SeasonPassReservationId { get; set; }
    }

    public class WristbandCodesRequest
    {
        // Both default empty; the controller requires at least one to be non-empty.
        [MaxLength(200)] public List<Guid> TicketIds { get; set; } = new();
        [MaxLength(200)] public List<Guid> ReservationIds { get; set; } = new();
    }
}
