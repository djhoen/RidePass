using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Me
{
    public class ShareCouponRequest
    {
        [Required, EmailAddress, MaxLength(200)]
        public string RecipientEmail { get; set; } = null!;

        [MaxLength(120)]
        public string? RecipientName { get; set; }

        [MaxLength(500)]
        public string? PersonalNote { get; set; }
    }
}
