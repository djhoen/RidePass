namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionSaleResponse
    {
        public Guid SaleId { get; set; }
        // Card-present PaymentIntent secret the cashier app confirms on the reader (null for cash).
        public string? ClientSecret { get; set; }
        public string? PaymentIntentId { get; set; }
        public int TotalCents { get; set; }
        // Total discount/comp applied (cents), for the POS to show on the confirmation.
        public int DiscountCents { get; set; }
        public string Status { get; set; } = "pending";   // 'paid' immediately for cash
        public int? OrderNumber { get; set; }              // assigned now for cash; after payment for card
    }
}
