using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Rental
{
    public class ReorderRentalProductsRequest
    {
        [Required, MinLength(1)]
        public List<ReorderRentalProductItem> Items { get; set; } = new();
    }

    public class ReorderRentalProductItem
    {
        [Required] public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
