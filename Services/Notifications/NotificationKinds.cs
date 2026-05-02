namespace Services.Notifications
{
    /// <summary>
    /// Canonical kind strings used on notification rows + the catalog of kinds whose email
    /// delivery is user-configurable (super admin emails today; tenant emails when added).
    /// Add new kinds here so they show up in the settings UI.
    /// </summary>
    public static class NotificationKinds
    {
        public const string DisputeOpened    = "dispute_opened";
        public const string DisputeLost      = "dispute_lost";
        public const string PayoutFailed     = "payout_failed";
        public const string LargeSale        = "large_sale";
        public const string RefundProcessed  = "refund_processed";
        public const string PayoutPaid       = "payout_paid";

        public record Descriptor(string Kind, string Label, string Description, string[] Audiences);

        public const string AudienceSuperAdmin  = "super_admin";
        public const string AudienceTenantAdmin = "tenant_admin";

        /// <summary>Every kind whose email delivery is user-configurable. Surfaced in the settings UI,
        /// filtered by the caller's role. Add new kinds + audiences here.</summary>
        public static readonly Descriptor[] Emailable =
        {
            new(DisputeOpened, "Dispute filed",
                "A customer disputed a charge and needs response.",
                new[] { AudienceSuperAdmin, AudienceTenantAdmin }),
            new(DisputeLost, "Chargeback lost",
                "A dispute resolved against us; the tenant has been debited.",
                new[] { AudienceSuperAdmin, AudienceTenantAdmin }),
            new(PayoutFailed, "Payout failed",
                "A tenant payout transitioned to failed status; needs investigation.",
                new[] { AudienceSuperAdmin }),
            new(LargeSale, "Large sale",
                "A successful sale above the configured large-sale threshold.",
                new[] { AudienceSuperAdmin }),
            new(RefundProcessed, "Refund issued",
                "A refund was issued to a customer on your charge.",
                new[] { AudienceTenantAdmin }),
            new(PayoutPaid, "Payout sent",
                "A payout has been sent to your bank.",
                new[] { AudienceTenantAdmin }),
        };

        public static Descriptor[] ForRole(string? role) => role switch
        {
            "super_admin"   => Emailable.Where(d => d.Audiences.Contains(AudienceSuperAdmin)).ToArray(),
            "tenant_admin"  => Emailable.Where(d => d.Audiences.Contains(AudienceTenantAdmin)).ToArray(),
            _               => Array.Empty<Descriptor>(),
        };
    }
}
