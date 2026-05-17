using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class UpdateAddressRequest
    {
        [MaxLength(200)] public string? AddressLine { get; set; }
        [MaxLength(200)] public string? AddressLine2 { get; set; }
        [MaxLength(120)] public string? City { get; set; }
        // Two-letter US state code (e.g. "CA", "TX"). Free-form for international.
        [MaxLength(40)] public string? State { get; set; }
        [MaxLength(20)] public string? PostalCode { get; set; }
        [MaxLength(2)]  public string? Country { get; set; }
    }
}
