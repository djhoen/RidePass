namespace webapi.Controllers.API.Data.Concession
{
    // List summary of a stock take (negative VarianceCents = a loss / shrinkage).
    public class ConcessionInventoryCountSummary
    {
        public Guid Id { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? Note { get; set; }
        public long VarianceCents { get; set; }
    }

    // Detail of a stock take: per-item expected vs counted variance + cost.
    public class ConcessionInventoryCountDetail
    {
        public Guid Id { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? Note { get; set; }
        public long TotalVarianceCents { get; set; }
        public List<Line> Lines { get; set; } = new();

        public class Line
        {
            public string Name { get; set; } = null!;
            public string Unit { get; set; } = "each";
            public decimal ExpectedQty { get; set; }
            public decimal CountedQty { get; set; }
            public decimal Variance { get; set; }
            public int UnitCostCents { get; set; }
            public long VarianceCents { get; set; }
        }
    }
}
