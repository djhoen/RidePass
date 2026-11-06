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
        // Tips taken out of THIS drawer at shift closes (Script0298: SUM of
        // tip_shift.tips_from_drawer_cents for the session, whoever's shift it was). An INPUT to
        // ExpectedCents: it is cash that left the drawer, and the reconciliation subtracts it
        // (CashController.BuildReconciliation). Set on open sessions too, since a shift can close
        // against a drawer before it is counted. HouseOwesCents is the turn-in's own pre-0298
        // figure, kept for history (0 on new rows) and null on an open session.
        public int? TipsFromDrawerCents { get; set; }
        public int? HouseOwesCents { get; set; }
    }
}
