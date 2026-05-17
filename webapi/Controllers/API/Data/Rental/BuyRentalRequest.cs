using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Rental
{
    public class BuyRentalRequest
    {
        [Required] public Guid ProductId { get; set; }
        [Required] public DateTime StartDate { get; set; }
        [Required] public DateTime EndDate { get; set; }

        [Range(1, 50)]
        public int Quantity { get; set; } = 1;

        [MaxLength(40)] public string? CouponCode { get; set; }
        [MaxLength(40)] public string? GiftCardCode { get; set; }
    }

    public class BuyRentalResponse
    {
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        public string ClientSecret { get; set; } = null!;
        public int AmountCents { get; set; }              // What Stripe charges
        public int RentalFeeCents { get; set; }
        public int DepositCents { get; set; }
        public int RiderServiceChargeCents { get; set; }
        public int GiftCardAppliedCents { get; set; }
    }

    public class MyRentalResponse
    {
        public Guid Id { get; set; }
        public Guid RedemptionToken { get; set; }
        public string ProductName { get; set; } = null!;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int Quantity { get; set; }
        public int AmountCents { get; set; }
        public int DepositCents { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }
}
