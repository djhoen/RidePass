namespace Services.Repositories.Data.CashData
{
    // A blind-count cash turn-in from a worker to a manager. The worker counts without
    // seeing ExpectedCents; the manager confirms receipt from their own login (no PIN
    // on the hand-off) and enters their count, which sets VarianceCents. manager_* and
    // confirmed_at stay null until that confirmation.
    public class CashTurnIn
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid CashSessionId { get; set; }
        public Guid? EventId { get; set; }
        public Guid WorkerUserId { get; set; }
        public Guid? ManagerUserId { get; set; }
        public int? ExpectedCents { get; set; }
        public int WorkerCountedCents { get; set; }
        public int? ManagerCountedCents { get; set; }
        public int? VarianceCents { get; set; }
        public string Status { get; set; } = "submitted";
        public string? Note { get; set; }
        public DateTime SubmittedAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }
    }
}
