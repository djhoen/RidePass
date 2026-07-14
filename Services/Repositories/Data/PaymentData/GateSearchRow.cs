namespace Services.Repositories.Data.PaymentData
{
    /// <summary>
    /// One hit on the gate's name/email lookup: a purchaser's order for one event, collapsed to a
    /// single row. The token is any ticket in that order, which is all the gate needs: scanning or
    /// selecting it resolves the whole event+purchaser scope, exactly as a QR scan would.
    /// </summary>
    public class GateSearchRow
    {
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartsAt { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        /// <summary>Any redemption token in this order: the anchor for the existing order lookup.</summary>
        public Guid AnchorToken { get; set; }
        /// <summary>Ticket count in this order, and how many are already checked in, so staff can see
        /// at a glance whether they're about to re-admit someone.</summary>
        public int ItemCount { get; set; }
        public int RedeemedCount { get; set; }
        /// <summary>The riders on the order (distinct names captured at registration), so a search for
        /// "Reed" shows whose order it's on when the buyer is a parent with a different surname.</summary>
        public string? RiderNames { get; set; }
    }
}
