using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// One entry appended to a rental's internal note thread. Staff-only: nothing here reaches
    /// the renter. Distinct from the condition notes recorded when gear comes back.
    /// </summary>
    public class AddShopRentalNoteRequest
    {
        [Required, MaxLength(4000)] public string Body { get; set; } = null!;
    }
}
