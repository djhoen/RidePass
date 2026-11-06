namespace webapi.Controllers.API.Data.Cash
{
    public class SubmitTurnInRequest
    {
        // Identifies which open session to turn in (matched by worker + event).
        public Guid? EventId { get; set; }
        // Blind count: the worker enters their counted cash without seeing the expected total.
        public int WorkerCountedCents { get; set; }
        public string? Note { get; set; }
        // Tips the worker is taking out of the drawer in cash. The server recomputes tips earned
        // (every unclaimed tip on their paid sales) and rejects anything outside 0..earned; the
        // remainder is recorded as house-owes. Older clients omit both fields, which reads as
        // "took none".
        public int TipsFromDrawerCents { get; set; }
        // Cash tips received outside the register (a jar, a hand-over). Informational.
        public int CashTipsDeclaredCents { get; set; }
    }
}
