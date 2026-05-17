using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    public class ReorderChoicesRequest
    {
        [Required, MinLength(1)]
        public List<ReorderChoiceItem> Items { get; set; } = new();
    }

    public class ReorderChoiceItem
    {
        [Required] public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
