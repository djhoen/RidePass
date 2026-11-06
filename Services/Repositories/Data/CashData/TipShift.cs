namespace Services.Repositories.Data.CashData
{
    // A person's shift report (Script0298): the unit a tip is settled on. Written once at shift
    // close for the person who punched in (UserId), by whichever account was signed in on the
    // terminal (ClosedByUserId). The figures are snapshotted FROM THE ROWS THE CLOSE CLAIMED
    // (concession_sale.tip_shift_id), never from the client, so a later refund or re-ring cannot
    // rewrite what a worker was told at the end of a shift. CashSessionId is the drawer the tips
    // came out of and is only set in drawer_at_close mode; in manager_later mode nothing leaves a
    // drawer and HouseOwesCents equals TipsEarnedCents.
    public class TipShift
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public Guid? ClosedByUserId { get; set; }
        public Guid? CashSessionId { get; set; }
        public DateOnly BusinessDate { get; set; }
        public DateTime ClosedAt { get; set; }
        public int TipsEarnedCents { get; set; }
        public int CardTipsCents { get; set; }
        // Cash tips the person says they received outside the register (a jar, a hand-over).
        public int CashTipsDeclaredCents { get; set; }
        // Tips taken out of the drawer at close (also logged as a 'drawer' tip_payout).
        public int TipsFromDrawerCents { get; set; }
        // TipsEarned - TipsFromDrawer: what the house still owes them for this shift.
        public int HouseOwesCents { get; set; }
        public string? Note { get; set; }
        // The register's id for the submit that wrote this row (unique per tenant); a retry with the
        // same id gets this row back instead of a second close. Null when none was sent.
        public Guid? ClientRequestId { get; set; }
    }

    // A tip_shift with the person's display name resolved, for the close-out response.
    public class TipShiftRow : TipShift
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
    }

    // One drawer's total drawer tip-outs, for the reconciliation.
    public class SessionTipOutRow
    {
        public Guid CashSessionId { get; set; }
        public long TipsFromDrawerCents { get; set; }
    }
}
