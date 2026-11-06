namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>Cash handling for the business date. Both lists are empty when the track takes no cash.</summary>
    public class EndOfDayCashSection
    {
        public List<EndOfDayCashSessionDto> Sessions { get; set; } = new();
        public List<EndOfDayCashTurnInDto> TurnIns { get; set; } = new();
        public long OpeningFloatCents { get; set; }
        public long WorkerCountedCents { get; set; }
        /// <summary>Sum of the manager counts that exist. Turn-ins still awaiting confirmation contribute nothing.</summary>
        public long ManagerCountedCents { get; set; }
        /// <summary>Cash tender on the day's sales and refunds, from the ledger: what SHOULD have been in the drawers.</summary>
        public long CashSalesCents { get; set; }
        /// <summary>
        /// Cash handed to staff as tips on the date (the ledger's cash 'tip_payout' rows: drawer
        /// tip-outs at close and manager cash settlements), as a positive figure. Cash that left
        /// the drawers and is therefore not in the counts above.
        /// </summary>
        public long TipsPaidOutCashCents { get; set; }
    }
}
