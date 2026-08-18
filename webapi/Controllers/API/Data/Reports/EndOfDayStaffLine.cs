namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>Who rang the day up. Only rows the ledger attributed to a seller appear.</summary>
    public class EndOfDayStaffLine
    {
        public Guid UserId { get; set; }
        /// <summary>Display name, falling back to the login email when the account has no name on it.</summary>
        public string Name { get; set; } = null!;
        public int SaleCount { get; set; }
        public int RefundCount { get; set; }
        public long GrossCents { get; set; }
        /// <summary>The part of GrossCents taken as cash, which is what this person should be handing in.</summary>
        public long CashCents { get; set; }
    }
}
