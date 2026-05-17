using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Purchase
{
    public class BuyGiftCardRequest
    {
        // Buyer-chosen denomination, validated server-side against tenant's min/max.
        [Range(1, int.MaxValue)]
        public int AmountCents { get; set; }

        [Required, MaxLength(120)]
        public string RecipientName { get; set; } = null!;

        [Required, EmailAddress, MaxLength(200)]
        public string RecipientEmail { get; set; } = null!;

        [MaxLength(500)]
        public string? PersonalNote { get; set; }

        // Optional UTC datetime to schedule delivery (e.g. send on a birthday). Null
        // means deliver as soon as the payment clears.
        public DateTime? ScheduledDeliveryAtUtc { get; set; }
    }

    public class BuyGiftCardResponse
    {
        public Guid GiftCardId { get; set; }
        public string ClientSecret { get; set; } = null!;
        public int AmountCents { get; set; }
    }
}
