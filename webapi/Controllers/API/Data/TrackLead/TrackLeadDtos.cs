using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.TrackLead
{
    public class SubmitTrackLeadRequest
    {
        [Required, MaxLength(120)]
        public string ContactName { get; set; } = null!;

        [Required, MaxLength(160)]
        public string TrackName { get; set; } = null!;

        [Required, EmailAddress, MaxLength(200)]
        public string Email { get; set; } = null!;

        [MaxLength(40)]
        public string? Phone { get; set; }

        [MaxLength(4000)]
        public string? Message { get; set; }
    }
}
