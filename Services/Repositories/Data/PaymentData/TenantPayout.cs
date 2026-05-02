namespace Services.Repositories.Data.PaymentData
{
    public class TenantPayout
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Status { get; set; } = null!;          // pending | processing | paid | failed | on_hold
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }
        public DateTime? PayoutDateUtc { get; set; }
        public int TotalGrossCents { get; set; }
        public int TotalStripeFeeCents { get; set; }
        public int TotalRidepassCutCents { get; set; }
        public int TotalAdjustmentCents { get; set; }
        public int NetPaidCents { get; set; }
        public string? ExternalReference { get; set; }
        public string? Memo { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public Guid? ApprovedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
