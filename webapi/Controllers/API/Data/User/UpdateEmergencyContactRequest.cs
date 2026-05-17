using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class UpdateEmergencyContactRequest
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;

        [Required, MaxLength(40)]
        public string Phone { get; set; } = null!;
    }
}
