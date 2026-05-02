using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Counter
{
    public class CreateCounterRiderRequest
    {
        [Required, EmailAddress, MaxLength(200)]
        public string Email { get; set; } = null!;

        [Required, MaxLength(120)]
        public string FirstName { get; set; } = null!;

        [Required, MaxLength(120)]
        public string LastName { get; set; } = null!;
    }
}
