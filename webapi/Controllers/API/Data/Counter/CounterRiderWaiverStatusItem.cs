namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>One ticket's waiver as the gate will judge it, for the counter's live status chips.</summary>
    public class CounterRiderWaiverStatusItem
    {
        public Guid PurchaseId { get; set; }
        public string? RiderName { get; set; }
        public bool IsMinor { get; set; }
        /// <summary>signed | not_required | link_sent | needs_signature (CounterRiderRules).</summary>
        public string Status { get; set; } = string.Empty;
        public bool SignedByGuardian { get; set; }
        public string? GuardianName { get; set; }
        public string? SignerName { get; set; }
        /// <summary>Why the gate would not admit this ticket right now (waiver or otherwise), if anything.</summary>
        public string? BlockReason { get; set; }
        public string? RequestStatus { get; set; }
        public DateTime? RequestSentAtUtc { get; set; }
        public string? RecipientEmail { get; set; }
        public string? Link { get; set; }
    }
}
