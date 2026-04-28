namespace webapi.Controllers.API.Data.Purchase
{
    public class PurchaseResponse
    {
        public Guid Id { get; set; }
        public string ProductName { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public int AmountCents { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? ValidOnDate { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
