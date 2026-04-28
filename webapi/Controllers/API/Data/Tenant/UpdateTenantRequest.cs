using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    public class UpdateTenantRequest
    {
        [Required]
        [RegularExpression(@"^[A-Za-z_]+/[A-Za-z_+\-0-9]+(?:/[A-Za-z_+\-0-9]+)?$",
            ErrorMessage = "Timezone must be an IANA name like 'America/Denver'.")]
        public string Timezone { get; set; } = null!;

        public bool RequireReservationForPasses { get; set; }
    }

    public class UpdateTenantLocationRequest
    {
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }
}
