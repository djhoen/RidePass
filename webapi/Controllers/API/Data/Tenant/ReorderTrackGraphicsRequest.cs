using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    public class ReorderTrackGraphicsRequest
    {
        [Required, MinLength(1)]
        public List<ReorderTrackGraphicItem> Items { get; set; } = new();
    }

    public class ReorderTrackGraphicItem
    {
        [Required] public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
