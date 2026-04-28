namespace webapi.Controllers.API.Data.Purchase
{
    public class CreatePurchaseResponse
    {
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        public string ClientSecret { get; set; } = null!;
        public int AmountCents { get; set; }
    }
}
