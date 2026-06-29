namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionInventoryItemResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string Unit { get; set; } = "each";
        public int CostCents { get; set; }
        public decimal OnHand { get; set; }
        public decimal? LowStockThreshold { get; set; }
        public bool IsLow { get; set; }   // threshold set and on-hand at/below it
        public bool IsActive { get; set; }
    }
}
