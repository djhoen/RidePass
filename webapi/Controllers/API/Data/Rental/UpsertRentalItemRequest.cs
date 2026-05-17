using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Rental
{
    public class UpsertRentalItemRequest
    {
        [Required, MaxLength(80)]
        public string Label { get; set; } = null!;

        [MaxLength(120)]
        public string? Serial { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        [Required, RegularExpression("^(available|maintenance|retired)$")]
        public string Status { get; set; } = "available";
    }

    public class RentalItemResponse
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string Label { get; set; } = null!;
        public string? Serial { get; set; }
        public string? Notes { get; set; }
        public string Status { get; set; } = null!;
    }
}
