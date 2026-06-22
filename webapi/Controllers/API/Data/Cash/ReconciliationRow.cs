namespace webapi.Controllers.API.Data.Cash
{
    // One reconciliation line for a worker's cash session at an event. A submitted/confirmed
    // turn-in has counts; a still-open session (no turn-in yet) shows expected-so-far with
    // null counts so the manager sees who still owes a turn-in. Expected = opening float plus
    // the worker's net cash (sales minus refunds) during the session window.
    public class ReconciliationRow
    {
        public Guid? TurnInId { get; set; }
        public Guid CashSessionId { get; set; }
        public Guid WorkerUserId { get; set; }
        public string? WorkerName { get; set; }
        public string Status { get; set; } = null!;   // open | submitted | confirmed
        public DateTime OpenedAtUtc { get; set; }
        public DateTime? SubmittedAtUtc { get; set; }
        public DateTime? ConfirmedAtUtc { get; set; }
        public int OpeningFloatCents { get; set; }
        public long ExpectedCents { get; set; }
        public int? WorkerCountedCents { get; set; }
        public int? ManagerCountedCents { get; set; }
        public long? VarianceCents { get; set; }      // (manager ?? worker) counted minus expected
    }
}
