namespace Services.Repositories.Data.PaymentData
{
    public class TenantLedgerEntry
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string EntryKind { get; set; } = null!;     // sale | refund | dispute_loss | dispute_fee | adjustment | sms_charge | email_charge | tip_payout
        public string? SourceKind { get; set; }            // pass | event_ticket | ... | tip_payout | null
        public Guid? SourceId { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public int GrossCents { get; set; }
        public int StripeFeeCents { get; set; }
        public int RidepassCutCents { get; set; }
        public int NetToTenantCents { get; set; }
        public Guid? AppliedTierId { get; set; }
        public long? CumulativeMonthlyVolumeAtSaleCents { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public Guid? PayoutId { get; set; }
        public string? Memo { get; set; }
        public string PaymentMethod { get; set; } = "stripe";    // stripe | cash | voucher | ... | payroll (tip_payout only)
        public Guid? SoldByUserId { get; set; }                  // cashier (sale) / refunder (refund) / whose drawer a tip payout left; null for online sales and payroll
        public DateTime CreatedAt { get; set; }
    }

    public class TenantBalanceSummary
    {
        public Guid TenantId { get; set; }
        public string TenantSubdomain { get; set; } = null!;
        public string TenantDisplayName { get; set; } = null!;
        public int AvailableBalanceCents { get; set; }   // sum of unpaid net_to_tenant (non-negative typically)
        public int LifetimeGrossCents { get; set; }
        public int LifetimeStripeFeeCents { get; set; }
        public int LifetimeRidepassCutCents { get; set; }
        public int LifetimePaidOutCents { get; set; }
        public int CurrentMonthGrossCents { get; set; }  // useful for "where in their tier are they now?"
    }
}
