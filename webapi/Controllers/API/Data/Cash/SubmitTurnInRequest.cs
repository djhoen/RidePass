namespace webapi.Controllers.API.Data.Cash
{
    // A blind-count drawer turn-in and nothing else (Script0298): tips are settled on the
    // person's shift close (POST Cash/ShiftClose), whichever drawer they were rung on.
    public class SubmitTurnInRequest
    {
        // Identifies which open session to turn in (matched by worker + event).
        public Guid? EventId { get; set; }
        // Blind count: the worker enters their counted cash without seeing the expected total.
        public int WorkerCountedCents { get; set; }
        public string? Note { get; set; }
        // Kept so a client built against the tips release still deserializes; the server ignores
        // both. Tips taken from a drawer are declared on ShiftClose now.
        [Obsolete("Ignored since Script0298: send tipsFromDrawerCents on Cash/ShiftClose instead.")]
        public int TipsFromDrawerCents { get; set; }
        [Obsolete("Ignored since Script0298: send cashTipsDeclaredCents on Cash/ShiftClose instead.")]
        public int CashTipsDeclaredCents { get; set; }
    }
}
