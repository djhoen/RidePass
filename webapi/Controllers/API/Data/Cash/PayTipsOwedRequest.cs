namespace webapi.Controllers.API.Data.Cash
{
    // A manager paying a worker what the house owes them. Amount must be positive and no more than
    // the worker's outstanding balance; the manager can't be the recipient.
    public class PayTipsOwedRequest
    {
        public Guid UserId { get; set; }
        public int AmountCents { get; set; }
        // cash | payroll. Cash comes out of the manager's own drawer and writes a ledger row.
        public string Source { get; set; } = "cash";
        public string? Note { get; set; }
    }
}
