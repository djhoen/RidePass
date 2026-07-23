using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>Walk-up gate redemption of a scanned season pass against one of today's events.
    /// The event is always chosen client-side (the scanner auto-selects when only one event is
    /// running) so the server never has to guess between same-day events.</summary>
    public class SeasonPassGateRedeemRequest
    {
        [Required] public Guid EventId { get; set; }
    }
}
