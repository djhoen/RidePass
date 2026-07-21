namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>One work order's estimated-vs-actual labor time, for the time-variance report.</summary>
    public class ShopLaborTimeRow
    {
        public Guid WorkOrderId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CustomerName { get; set; } = "";
        public string? BikeLabel { get; set; }
        public string Status { get; set; } = "";
        public string? TechName { get; set; }
        /// <summary>Sum of the labor lines' estimated minutes (0 when none were estimated).</summary>
        public int EstimatedMinutes { get; set; }
        public int ActualMinutes { get; set; }
    }
}
