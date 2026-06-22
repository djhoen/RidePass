namespace webapi.Controllers.API.Data.Redemption
{
    // Per-item outcome of an offline-admission sync.
    //   "admitted"       - this sync recorded the admit, OR an idempotent re-sync of your own
    //   "conflict"       - another device admitted first (first-to-sync wins); see Redeemed*
    //   "not_found"      - token not in this tenant
    //   "not_admissible" - the purchase isn't in a paid state (cancelled/refunded/pending)
    public class BatchAdmitResult
    {
        public string? ClientRef { get; set; }
        public Guid RedemptionToken { get; set; }
        public string Outcome { get; set; } = null!;
        public Guid? RedeemedByUserId { get; set; }
        public DateTime? RedeemedAtUtc { get; set; }
    }
}
