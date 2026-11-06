using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // A staff member punching their POS code at a shared register (Script0298). The terminal
    // account sends it; the response names the person and hands back the cashier token the
    // register attaches to every sale they ring from then on.
    public class ConcessionStaffCodeVerifyRequest
    {
        [Required, MaxLength(8)] public string Code { get; set; } = null!;
    }
}
