using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Counter
{
    public class CounterCartItem
    {
        // "day_pass" or "event_ticket"
        [Required]
        public string Kind { get; set; } = null!;

        // For day_pass: the DayPassProduct id. For event_ticket: the EventTicketTier id.
        [Required]
        public Guid ItemId { get; set; }

        [Range(1, 100)]
        public int Quantity { get; set; } = 1;
    }
}
