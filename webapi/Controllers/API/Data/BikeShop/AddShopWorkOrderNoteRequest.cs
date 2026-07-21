using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>One entry appended to a work order's internal notes thread.</summary>
    public class AddShopWorkOrderNoteRequest
    {
        [Required, MaxLength(4000)] public string Body { get; set; } = null!;
    }
}
