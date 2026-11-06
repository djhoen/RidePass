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
        /// <summary>Shortfall recorded on today's turn-ins: tips the drawer could not cover.</summary>
        public long HouseOwesCents { get; set; }
        /// <summary>House-owes (from any day) a manager paid them today.</summary>
        public long SettledTodayCents { get; set; }
    }
}
