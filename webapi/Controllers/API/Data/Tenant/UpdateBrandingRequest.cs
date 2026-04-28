using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    public class UpdateBrandingRequest
    {
        [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")]
        public string PrimaryColor { get; set; } = null!;

        [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")]
        public string SecondaryColor { get; set; } = null!;

        [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")]
        public string AccentColor { get; set; } = null!;

        [MaxLength(500)]
        public string? Tagline { get; set; }

        [Required, RegularExpression("^(light|dark)$")]
        public string ThemeMode { get; set; } = null!;
    }
}
