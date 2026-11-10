namespace Services.Repositories.Data.PricingData
{
    /// <summary>A venue's peak day (Script0316): a weekday or a date when a day pass costs more.</summary>
    public class DayPriceRule
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Label { get; set; } = "";
        /// <summary>0 = Sunday .. 6 = Saturday; null when OnDate is set.</summary>
        public int? Weekday { get; set; }
        public DateTime? OnDate { get; set; }
        /// <summary>Null = every event type.</summary>
        public Guid? EventTypeId { get; set; }
        /// <summary>'amount' (cents) | 'percent' (basis points).</summary>
        public string AdjustKind { get; set; } = "amount";
        public int AdjustValue { get; set; }
        public bool IsActive { get; set; } = true;
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
