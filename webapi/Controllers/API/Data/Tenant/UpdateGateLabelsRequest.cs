using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    public class UpdateGateLabelsRequest
    {
        // Blank/whitespace clears the override back to the platform default
        // ("Riding Pass" / "Spectator Pass").
        [MaxLength(40)]
        public string? RiderGateLabel { get; set; }

        [MaxLength(40)]
        public string? SpectatorGateLabel { get; set; }
    }
}
