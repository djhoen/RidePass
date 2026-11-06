using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Put one serialized unit on the sales floor (used &amp; ex-rental). Price, grade and title are
    /// validated in the controller through ListingRules so every rejection reads the same.
    /// </summary>
    public class CreateShopListingRequest
    {
        [Required] public Guid ItemId { get; set; }
        public int AskingPriceCents { get; set; }
        /// <summary>excellent | good | fair</summary>
        public string? ConditionGrade { get; set; }
        [MaxLength(4000)] public string? ConditionNotes { get; set; }
        /// <summary>Blank = "{product} {variant} · {unit}".</summary>
        [MaxLength(200)] public string? Title { get; set; }
        /// <summary>false = counter only.</summary>
        public bool Published { get; set; } = true;
    }
}
