using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Bulk reorder of a product's gallery; the whole list is sent so the server
    /// never has to infer positions from a partial update.</summary>
    public class ShopImageReorderRequest
    {
        [Required] public List<ShopImageReorderItem> Items { get; set; } = new();
    }
}
