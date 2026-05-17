using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.EventType
{
    public class UpsertEventTypeRequest
    {
        [Required, MaxLength(80)]
        public string Name { get; set; } = null!;

        [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")]
        public string Color { get; set; } = null!;

        // Optional default cover image; falls back to a flat-color card on the home page when null.
        public string? ImageUrl { get; set; }

        public int SortOrder { get; set; } = 100;
    }
}
