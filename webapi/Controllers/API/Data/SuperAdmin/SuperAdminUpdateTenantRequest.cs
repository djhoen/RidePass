using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SuperAdmin
{
    /// <summary>
    /// Super-admin edit of a tenant's core details. Subdomain and tenant type are
    /// intentionally NOT editable here (identity / provisioning-locked).
    /// Named distinctly from the tenant-facing UpdateTenantRequest so Swagger's
    /// short-name schema IDs don't collide.
    /// </summary>
    public class SuperAdminUpdateTenantRequest
    {
        [Required, MaxLength(200)]
        public string DisplayName { get; set; } = null!;

        [Required, RegularExpression("^(active|suspended|pending)$")]
        public string Status { get; set; } = null!;

        [Required, MaxLength(64)]
        public string Timezone { get; set; } = null!;

        // Whether the tenant appears in public discovery (map / featured / search / events).
        public bool IsPublished { get; set; }

        // Platform service charge in basis points (0..10000 = 0%..100%).
        [Range(0, 10000)]
        public int ServiceChargeBps { get; set; }

        // Monthly cap in cents; null = no cap.
        public int? MonthlyServiceChargeCapCents { get; set; }

        [MaxLength(300)] public string? AddressLine { get; set; }
        [MaxLength(120)] public string? City { get; set; }
        [MaxLength(120)] public string? Region { get; set; }
        [MaxLength(40)] public string? PostalCode { get; set; }
        [MaxLength(80)] public string? Country { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [MaxLength(200)] public string? ContactEmail { get; set; }
        [MaxLength(40)] public string? Phone { get; set; }

        // LoamMx destination id. Non-empty marks this tenant as a LoamPassMx track; blank clears it.
        [MaxLength(64)] public string? LoampassMxDestinationId { get; set; }
    }
}
