using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class UpdatePhoneRequest
    {
        [Required, MaxLength(40)]
        public string Phone { get; set; } = null!;
    }
}
