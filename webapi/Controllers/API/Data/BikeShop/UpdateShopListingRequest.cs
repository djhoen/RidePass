using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Edit a listing: price, grade, notes, title, published. Never the unit.</summary>
    public class UpdateShopListingRequest
    {
        public int AskingPriceCents { get; set; }
        public string? ConditionGrade { get; set; }
        [MaxLength(4000)] public string? ConditionNotes { get; set; }
        /// <summary>Blank = back to the default "{product} {variant} · {unit}".</summary>
        [MaxLength(200)] public string? Title { get; set; }
        public bool Published { get; set; } = true;
    }
}
