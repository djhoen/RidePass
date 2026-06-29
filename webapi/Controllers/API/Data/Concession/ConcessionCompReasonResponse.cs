namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionCompReasonResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string DefaultKind { get; set; } = "full";   // 'full' | 'percent' | 'amount'
        public int DefaultValue { get; set; }                // bps when percent, cents when amount
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }
}
