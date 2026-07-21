using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>Create or edit one serialized unit (a specific bike).</summary>
    public class UpsertShopItemRequest
    {
        [Required, MaxLength(160)] public string Label { get; set; } = null!;
        [MaxLength(120)] public string? Serial { get; set; }
        [MaxLength(2000)] public string? Notes { get; set; }

        // Omitted on create (a new unit starts 'available'); settable on edit to move a unit
        // to/from maintenance or retire it. 'sold' / 'rented_out' are driven by transactions,
        // not set by hand here.
        [RegularExpression("^(available|maintenance|retired)$")]
        public string? Status { get; set; }

        [Range(0, 100_000_000)] public int? AcquiredCostCents { get; set; }
    }
}
