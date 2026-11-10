namespace webapi.Controllers.API.Data.DayPricing
{
    public class DayPriceRuleItem
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = "";
        public int? Weekday { get; set; }
        public DateOnly? OnDate { get; set; }
        public Guid? EventTypeId { get; set; }
        public string AdjustKind { get; set; } = "amount";
        public int AdjustValue { get; set; }
        public bool IsActive { get; set; }
    }
}
