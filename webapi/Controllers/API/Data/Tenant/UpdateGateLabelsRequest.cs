using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    public class UpdateGateLabelsRequest
    {
        // Blank/whitespace clears the override back to the platform default
        // ("Rider Gate" / "Spectator Gate").
        [MaxLength(40)]
        public string? RiderGateLabel { get; set; }

        [MaxLength(40)]
        public string? SpectatorGateLabel { get; set; }
    }
}
