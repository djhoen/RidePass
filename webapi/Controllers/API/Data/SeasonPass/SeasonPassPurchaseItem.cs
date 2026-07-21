namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>
    /// One created pass, returned from checkout so the client can drive the registration step:
    /// it needs the id to register against, and the product's waiver flag to know whether to
    /// ask this holder for a signature.
    /// </summary>
    public class SeasonPassPurchaseItem
    {
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = null!;
        public bool RequiresWaiver { get; set; }
    }
}
