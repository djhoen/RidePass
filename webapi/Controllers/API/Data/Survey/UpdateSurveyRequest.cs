using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    public class UpdateSurveyRequest
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Title { get; set; } = null!;

        [MaxLength(4000)]
        public string? Description { get; set; }

        public DateTime? ClosesAtUtc { get; set; }

        public bool RequireEmail { get; set; }
    }
}
