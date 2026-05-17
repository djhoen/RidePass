using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class CreateAccountRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = null!;

        [Required, MinLength(8)]
        public string Password { get; set; } = null!;

        [Required]
        public string FirstName { get; set; } = null!;

        [Required]
        public string LastName { get; set; } = null!;

        [Required]
        public DateTime Birthdate { get; set; }

        // Required so we can SMS the rider for waitlist promotions, race-day alerts, etc.
        [Required, MaxLength(40)]
        public string Phone { get; set; } = null!;

        [Required, MaxLength(120)]
        public string EmergencyContactName { get; set; } = null!;

        [Required, MaxLength(40)]
        public string EmergencyContactPhone { get; set; } = null!;
    }
}
