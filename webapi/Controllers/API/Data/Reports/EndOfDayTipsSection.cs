namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// Tips for the business date: what staff earned, what they were handed, and what the house
    /// still owes them. Earned is read from the day's PAID sales per seller (so a refund can never
    /// be charged to whoever processed it); the two payout figures come from the tip_payout log
    /// for the date; OwedOutstanding is the all-time balance as of now, because a shortfall from an
    /// earlier shift is still owed whichever day the report is for.
    /// </summary>
    public class EndOfDayTipsSection
    {
        /// <summary>
        /// The sum of ByWorker's EarnedCents, so the table always adds up to the headline. On a day
        /// with refunds this can differ slightly from Totals.TipsCents, which is the ledger's own
        /// net figure (a refund's prorated tip is subtracted there on the day of the refund, here
        /// the refunded sale simply stops counting).
        /// </summary>
        public long EarnedCents { get; set; }
        /// <summary>Tips workers took out of their own drawers at close today.</summary>
        public long PaidFromDrawerCents { get; set; }
        /// <summary>House-owes a manager settled today, in cash or through payroll.</summary>
        public long PaidByManagerCents { get; set; }
        /// <summary>Everything still owed to staff across all time, as of now.</summary>
        public long OwedOutstandingCents { get; set; }
        public List<EndOfDayTipsWorkerLine> ByWorker { get; set; } = new();
    }
}
