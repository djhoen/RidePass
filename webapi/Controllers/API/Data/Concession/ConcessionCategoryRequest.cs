using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionCategoryRequest
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
