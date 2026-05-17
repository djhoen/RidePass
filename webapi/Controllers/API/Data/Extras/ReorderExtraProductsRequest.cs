using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Extras
{
    public class ReorderExtraProductsRequest
    {
        [Required, MinLength(1)]
        public List<ReorderExtraProductItem> Items { get; set; } = new();
    }

    public class ReorderExtraProductItem
    {
        [Required] public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
