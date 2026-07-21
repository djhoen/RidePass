using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class LoginRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = null!;

        [Required]
        public string Password { get; set; } = null!;

        /// <summary>Keep this device signed in for an extended period instead of the default
        /// 24 hours. Deliberately extends the SESSION rather than storing any credential: a
        /// password kept client-side would be readable by any script on the page, and unlike a
        /// session it is reusable elsewhere. Off by default so a shared counter machine stays
        /// short-lived unless someone opts in.</summary>
        public bool RememberMe { get; set; }
    }
}
