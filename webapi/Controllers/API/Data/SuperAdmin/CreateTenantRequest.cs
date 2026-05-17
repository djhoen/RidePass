using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class CreateTenantRequest
    {
        [Required, RegularExpression("^[a-z][a-z0-9-]{1,62}$",
            ErrorMessage = "Subdomain must be lowercase letters, digits, and hyphens. Start with a letter.")]
        public string Subdomain { get; set; } = null!;

        [Required, MaxLength(200)]
        public string DisplayName { get; set; } = null!;

        // Drives provisioning defaults at creation time. Defaults to motocross
        // since that's RidePass's original use case.
        [Required, RegularExpression("^(motocross|mountain_bike)$")]
        public string TenantType { get; set; } = "motocross";

        [Required, MaxLength(80)]
        public string Timezone { get; set; } = "UTC";

        // Optional first tenant_admin provisioning.
        [EmailAddress, MaxLength(200)]
        public string? AdminEmail { get; set; }

        [MaxLength(120)]
        public string? AdminFirstName { get; set; }

        [MaxLength(120)]
        public string? AdminLastName { get; set; }
    }
}
