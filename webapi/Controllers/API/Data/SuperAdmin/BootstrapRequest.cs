using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class BootstrapRequest
    {
        [Required, EmailAddress, MaxLength(200)]
        public string Email { get; set; } = null!;

        [Required, MinLength(8), MaxLength(200)]
        public string Password { get; set; } = null!;

        [Required, MaxLength(120)]
        public string FirstName { get; set; } = null!;

        [Required, MaxLength(120)]
        public string LastName { get; set; } = null!;
    }
}
