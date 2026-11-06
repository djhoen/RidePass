namespace webapi.Controllers.API.Data.Cash
{
    // Recording a tip jar split (POST Cash/TipJar): the day's jar total, divided evenly among the
    // people listed, in the order given (the cents that do not divide go one each to the first
    // people). No ledger row, no liability: the staff already took the cash.
    public class TipJarSplitRequest
    {
        // yyyy-MM-dd; defaults to today at the track.
        public string? Date { get; set; }
        public int TotalCents { get; set; }
        public List<Guid> UserIds { get; set; } = new();
        public string? Note { get; set; }
    }
}
