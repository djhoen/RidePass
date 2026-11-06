using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>The listing's photo ids in display order; the whole list is sent.</summary>
    public class ShopListingPhotoOrderRequest
    {
        [Required] public List<Guid> PhotoIds { get; set; } = new();
    }
}
