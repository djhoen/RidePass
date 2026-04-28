using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Purchase
{
    public class CreateTicketPurchaseRequest
    {
        [Required]
        public Guid TierId { get; set; }

        // Required for guest checkout; ignored when the request carries a valid JWT.
        [EmailAddress, MaxLength(200)]
        public string? Email { get; set; }

        [MaxLength(120)]
        public string? Name { get; set; }
    }
}
