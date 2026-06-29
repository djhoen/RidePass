using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // Create/update a modifier group (e.g. "Choose a side"). Options are managed via the
    // option endpoints under the group.
    public class ConcessionModifierGroupRequest
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;

        [Range(0, 50)]
        public int MinSelect { get; set; }

        // null = unlimited.
        [Range(1, 50)]
        public int? MaxSelect { get; set; }

        public bool IsRequired { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
