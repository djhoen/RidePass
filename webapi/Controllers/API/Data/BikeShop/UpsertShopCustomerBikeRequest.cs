using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Create or update a customer's bike. Omitting <see cref="Id"/> and supplying a serial that
    /// already exists updates THAT bike rather than creating a second record for the same physical
    /// machine, so its repair history stays in one place.
    /// </summary>
    public class UpsertShopCustomerBikeRequest
    {
        public Guid? Id { get; set; }

        public Guid? CustomerUserId { get; set; }
        [MaxLength(160)] public string? CustomerName { get; set; }
        [MaxLength(40)] public string? CustomerPhone { get; set; }

        /// <summary>Optional: plenty of older bikes arrive with no readable serial.</summary>
        [MaxLength(120)] public string? Serial { get; set; }

        [MaxLength(80)] public string? Brand { get; set; }
        [MaxLength(160)] public string? Model { get; set; }
        [Range(1900, 2100)] public int? ModelYear { get; set; }
        [MaxLength(60)] public string? Color { get; set; }
        [MaxLength(40)] public string? Size { get; set; }
        [MaxLength(2000)] public string? Notes { get; set; }

        /// <summary>The serialized unit this bike left the shop as, when we sold it.</summary>
        public Guid? SoldItemId { get; set; }
    }
}
