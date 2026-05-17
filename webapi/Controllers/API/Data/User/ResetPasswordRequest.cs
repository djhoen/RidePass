using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class ResetPasswordRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = null!;
    }
}
