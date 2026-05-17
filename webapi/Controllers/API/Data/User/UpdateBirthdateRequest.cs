using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class UpdateBirthdateRequest
    {
        [Required]
        public System.DateTime Birthdate { get; set; }
    }
}
