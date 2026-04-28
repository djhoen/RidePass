using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Event
{
    public class UpsertEventRequest
    {
        [Required]
        public Guid EventTypeId { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = null!;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Required]
        public DateTime StartsAtUtc { get; set; }

        [Required]
        public DateTime EndsAtUtc { get; set; }

        public bool AllDay { get; set; }

        [Range(1, int.MaxValue)]
        public int? Capacity { get; set; }

        [MaxLength(120)]
        public string? LocationLabel { get; set; }

        [RegularExpression("^(scheduled|cancelled)$")]
        public string Status { get; set; } = "scheduled";
    }
}
