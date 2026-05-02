using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Counter
{
    public class RiderLookupRequest
    {
        [Required, EmailAddress, MaxLength(200)]
        public string Email { get; set; } = null!;
    }
}
