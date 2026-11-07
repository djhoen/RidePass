namespace Services.Repositories.Data.PaymentData
{
    /// <summary>
    /// One hit on the gate's name/email lookup for a SEASON PASS valid today. The redemption token is
    /// the pass's own, so selecting the row opens exactly the pass card a QR scan of it would.
    /// </summary>
    public class GatePassSearchRow
    {
        public Guid PassPurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        /// <summary>The named holder (holder_first/last_name), when the pass captured one.</summary>
        public string? HolderName { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string PurchaserEmail { get; set; } = null!;
        /// <summary>The linked account's current email, when it differs from the checkout email.</summary>
        public string? AccountEmail { get; set; }
        public string ProductName { get; set; } = null!;
        public string ProductKind { get; set; } = null!;
        public int? CreditsRemaining { get; set; }
        public DateTime ValidToDate { get; set; }
        /// <summary>Already admitted through the gate today (a checked_in reservation today).</summary>
        public bool CheckedInToday { get; set; }
    }
}
