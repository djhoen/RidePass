namespace webapi.Controllers.API.Data.Concession
{
    // A tab in full: the order detail (status, totals, flat lines) plus the tab-specific fields and
    // the same lines grouped by round, in firing order.
    public class ConcessionTabDetail : ConcessionOrderDetail
    {
        public DateTime? OpenedAtUtc { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
        public string? HoldCardLabel { get; set; }
        public int? HoldAmountCents { get; set; }
        public List<Round> Rounds { get; set; } = new();

        public class Round
        {
            public int RoundNumber { get; set; }
            public DateTime FiredAtUtc { get; set; }
            public List<Line> Lines { get; set; } = new();
        }
    }
}
