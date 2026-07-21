using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Counter
{
    public class CounterSaleRequest
    {
        [Required]
        public Guid RiderId { get; set; }

        [Required, MinLength(1)]
        public List<CounterCartItem> Items { get; set; } = new();

        // Set true if the rider is signing the active waiver as part of this sale.
        public bool SignWaiver { get; set; }

        // Required when SignWaiver=true: base64 PNG data URL captured from the signature pad.
        public string? SignatureDataUrl { get; set; }

        // Required when SignWaiver=true and the rider is under 18: the parent's name + phone.
        // The signature itself is the parent's.
        public string? ParentName { get; set; }
        public string? ParentPhone { get; set; }

        public Guid? RewardRedemptionId { get; set; }

        // Store credit as a tender: the account the cashier looked up and how much of its
        // balance to apply. The server re-verifies the balance and caps at the sale total.
        public Guid? CreditAccountId { get; set; }
        public int CreditCents { get; set; }

        // 'stripe' (default) or 'cash'. Cash means the tenant collected the rider's payment
        // directly; the platform records the service charge as ridepass_cut owed by the tenant.
        public string PaymentMethod { get; set; } = "stripe";
    }
}
