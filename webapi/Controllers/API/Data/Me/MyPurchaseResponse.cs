namespace webapi.Controllers.API.Data.Me
{
    public class MyPurchaseResponse
    {
        public string Kind { get; set; } = null!;
        public Guid Id { get; set; }
        public string ItemName { get; set; } = null!;
        public Guid? EventId { get; set; }
        public DateTime? EventStartsAtUtc { get; set; }
        public DateTime? ValidOnDate { get; set; }
        public int AmountCents { get; set; }
        public string Status { get; set; } = null!;
        public Guid RedemptionToken { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
