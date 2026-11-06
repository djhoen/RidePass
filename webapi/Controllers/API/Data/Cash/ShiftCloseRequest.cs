namespace webapi.Controllers.API.Data.Cash
{
    // A person closing their shift (Script0298): settles every unclaimed tip of theirs onto a
    // tip_shift. The person is the cashier token's user when one is sent, else the signed-in
    // account. The server recomputes tips earned from the rows it claims; nothing here is
    // trusted for that.
    public class ShiftCloseRequest
    {
        public string? CashierToken { get; set; }
        // A fresh uuid per close dialog, so a retry of the same submit (timeout, double tap) gets
        // the shift the first one wrote instead of settling twice. Optional; null = no replay guard.
        public Guid? ClientRequestId { get; set; }
        // In drawer_at_close mode, which of the terminal's drawers the tips come out of: the one
        // opened for this event, falling back to the no-event drawer when none is open for it.
        // Null = the no-event drawer. Ignored in manager_later mode.
        public Guid? EventId { get; set; }
        // Cash tips received outside the register (a jar, a hand-over). Informational.
        public int CashTipsDeclaredCents { get; set; }
        // Tips taken out of the TERMINAL account's open drawer in cash. Must be 0 in manager_later
        // mode; in drawer_at_close mode it is validated against the claimed tips and the remainder
        // is recorded as house-owes.
        public int TipsFromDrawerCents { get; set; }
        public string? Note { get; set; }
    }
}
