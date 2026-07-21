using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Redemption
{
    public class LinkWristbandRequest
    {
        [Required] public Guid TicketId { get; set; }
        [Required, MaxLength(200)] public string Code { get; set; } = null!;
    }

    public class UnlinkWristbandRequest
    {
        [Required] public Guid TicketId { get; set; }
    }

    public class WristbandCodesRequest
    {
        [Required, MinLength(1), MaxLength(200)] public List<Guid> TicketIds { get; set; } = new();
    }
}
