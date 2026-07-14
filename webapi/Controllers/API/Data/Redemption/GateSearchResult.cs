namespace webapi.Controllers.API.Data.Redemption
{
    /// <summary>
    /// A match on the gate's name/email lookup: one purchaser's order for one of today's events.
    /// AnchorToken is fed straight back into Order/{token}, so selecting a result lands the operator
    /// on exactly the same check-in screen a QR scan would have.
    /// </summary>
    public class GateSearchResult
    {
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public DateTime EventStartsAtUtc { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        public Guid AnchorToken { get; set; }
        public int ItemCount { get; set; }
        public int RedeemedCount { get; set; }
        /// <summary>Riders named on the order, when registration captured them.</summary>
        public string? RiderNames { get; set; }
    }
}
