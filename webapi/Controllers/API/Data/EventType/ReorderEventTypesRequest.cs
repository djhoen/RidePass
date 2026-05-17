using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.EventType
{
    public class ReorderEventTypesRequest
    {
        [Required, MinLength(1)]
        public List<ReorderEventTypeItem> Items { get; set; } = new();
    }

    public class ReorderEventTypeItem
    {
        [Required] public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
