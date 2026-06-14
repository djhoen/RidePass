using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.PlatformBranding
{
    public class UpsertTestimonialRequest
    {
        [Required, MaxLength(120)] public string RiderName { get; set; } = null!;
        [Required, MaxLength(1000)] public string Quote { get; set; } = null!;
        [Range(1, 5)] public int Rating { get; set; } = 5;
        public bool IsActive { get; set; } = true;
    }
}
