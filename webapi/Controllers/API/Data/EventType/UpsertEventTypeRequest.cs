using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.EventType
{
    public class UpsertEventTypeRequest
    {
        [Required, MaxLength(80)]
        public string Name { get; set; } = null!;

        [Required, RegularExpression("^#[0-9A-Fa-f]{6}$")]
        public string Color { get; set; } = null!;

        public int SortOrder { get; set; } = 100;
    }
}
