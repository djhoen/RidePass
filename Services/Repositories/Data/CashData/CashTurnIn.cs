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

        // Pre-Script0298 history only: tips were once settled on the turn-in (Script0294/0296).
        // Since Script0298 they are settled on tip_shift and every new turn-in carries 0 here;
        // moved rows keep tips_earned / house_owes for the record, and their tips_from_drawer was
        // zeroed because the reconciliation reads that figure from tip_shift now.
        public int TipsEarnedCents { get; set; }
        public int CardTipsCents { get; set; }
        // Cash tips the worker says they received outside the register. Informational only.
        public int CashTipsDeclaredCents { get; set; }
        // Tips the worker took out of the drawer at close (also logged as a 'drawer' tip_payout).
        public int TipsFromDrawerCents { get; set; }
        // TipsEarned - TipsFromDrawer: what the house still owes them for this shift.
        public int HouseOwesCents { get; set; }
    }
}
