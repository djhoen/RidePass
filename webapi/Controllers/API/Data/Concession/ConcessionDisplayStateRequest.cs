using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionDisplayStateRequest
    {
        // Opaque POS-built snapshot of the in-progress order (lines + totals + status). The server
        // just relays it to the paired display; it is never trusted for money.
        [MaxLength(65536)]
        public string? StateJson { get; set; }
    }
}
