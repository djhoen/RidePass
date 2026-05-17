using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    public class ReorderSeasonPassProductsRequest
    {
        [Required, MinLength(1)]
        public List<ReorderSeasonPassProductItem> Items { get; set; } = new();
    }

    public class ReorderSeasonPassProductItem
    {
        [Required] public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
