using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Purchase
{
    public class CreatePurchaseRequest
    {
        [Required]
        public Guid ProductId { get; set; }

        public DateTime? ValidOnDate { get; set; }

        // For reservation-bound purchases (tenant.require_reservation_for_passes = true).
        public Guid? EventId { get; set; }

        [Range(1, 50)]
        public int Quantity { get; set; } = 1;
    }
}
