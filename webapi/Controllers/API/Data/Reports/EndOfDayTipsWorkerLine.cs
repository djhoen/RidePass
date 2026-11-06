namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>One staff member's tips for the business date.</summary>
    public class EndOfDayTipsWorkerLine
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = null!;
        /// <summary>Tips on the sales they rang today, net of refunds.</summary>
        public long EarnedCents { get; set; }
        /// <summary>Tips they took out of their drawer at close today.</summary>
        public long FromDrawerCents { get; set; }
        /// <summary>House-owes recorded on today's shift closes: tips not taken from a drawer, to be paid by a manager.</summary>
        public long HouseOwesCents { get; set; }
        /// <summary>House-owes (from any day) a manager paid them today.</summary>
        public long SettledTodayCents { get; set; }
        /// <summary>Their share of today's tip jar splits.</summary>
        public long JarShareCents { get; set; }
        /// <summary>Cash tips they declared receiving outside the register on today's shift closes.</summary>
        public long DeclaredCashTipsCents { get; set; }
    }
}
