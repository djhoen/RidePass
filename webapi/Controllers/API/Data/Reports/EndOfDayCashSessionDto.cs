namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// A cash drawer session opened on the business date. There is deliberately no
    /// expected-vs-counted pair here: cash_session stores only the opening float and the session
    /// window (Script0131). The counted figures live on the turn-in rows, and "expected" is derived
    /// by the reconciliation report rather than persisted on either table.
    /// </summary>
    public class EndOfDayCashSessionDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserName { get; set; } = null!;
        public string? EventTitle { get; set; }
        public string? DeviceId { get; set; }
        public long OpeningFloatCents { get; set; }
        /// <summary>open | turned_in | closed</summary>
        public string Status { get; set; } = null!;
        public DateTime OpenedAtUtc { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
    }
}
