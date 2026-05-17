using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Pass
{
    public class ReorderPassProductsRequest
    {
        [Required, MinLength(1)]
        public List<ReorderPassProductItem> Items { get; set; } = new();
    }

    public class ReorderPassProductItem
    {
        [Required] public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
