using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class ConfirmPasswordResetRequest
    {
        [Required] public string Token { get; set; } = null!;
        [Required, MinLength(8)] public string NewPassword { get; set; } = null!;
    }
}
