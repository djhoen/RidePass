namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionDiscountPresetResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string Kind { get; set; } = "percent";   // 'percent' | 'amount'
        public int Value { get; set; }                   // bps when percent, cents when amount
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }
}
