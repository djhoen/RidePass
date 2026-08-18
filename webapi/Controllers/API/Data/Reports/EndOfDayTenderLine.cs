namespace webapi.Controllers.API.Data.Reports
{
    /// <summary>
    /// How the day was PAID FOR, a different cut of the same money than the revenue lines. A sale
    /// settled half on a gift card and half on a card contributes to two tenders, so the tender
    /// lines sum to net sales rather than matching any single revenue line.
    /// </summary>
    public class EndOfDayTenderLine
    {
        /// <summary>card | cash | gift_card | credit</summary>
        public string Method { get; set; } = null!;
        public string Label { get; set; } = null!;
        /// <summary>Net of refunds, which land in the same bucket with a negative amount.</summary>
        public long AmountCents { get; set; }
        public int Count { get; set; }
    }
}
