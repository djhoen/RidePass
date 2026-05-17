using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    public class ReorderGalleryRequest
    {
        [Required, MinLength(1)]
        public List<ReorderGalleryItem> Items { get; set; } = new();
    }

    public class ReorderGalleryItem
    {
        [Required] public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
