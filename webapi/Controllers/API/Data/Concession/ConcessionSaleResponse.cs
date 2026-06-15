namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionSaleResponse
    {
        public Guid SaleId { get; set; }
        // Card-present PaymentIntent secret the cashier app confirms on the reader.
        public string? ClientSecret { get; set; }
        public string? PaymentIntentId { get; set; }
        public int TotalCents { get; set; }
    }
}
