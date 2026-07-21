namespace Services.Repositories.Data.CreditData
{
    // Store credit: per-tenant customer balance, ledgered append-only (tenant_credit_entry)
    // with a cached floor-guarded balance, mirroring the stock/on-hand pattern. Credit is a
    // tender, not money: issuing and redeeming it never writes tenant_ledger_entry rows.

    public class TenantCreditAccount
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public string? Email { get; set; }        // lowercased
        public string? Phone { get; set; }        // digits only
        public string? DisplayName { get; set; }
        public int BalanceCents { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // Credit applied to a multi-row checkout (gate counter / online event checkout): the anchor
    // the redeem entry references, findable again by PaymentIntent id for failure reversal and
    // the success-side balancing ledger entry. NULL PI = a cash counter sale settled immediately.
    public class CheckoutCreditTender
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid CreditAccountId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public int CreditAppliedCents { get; set; }
        public string Context { get; set; } = "counter";   // counter | event_checkout
        public DateTime CreatedAt { get; set; }
    }

    public class TenantCreditEntry
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid AccountId { get; set; }
        public int DeltaCents { get; set; }
        public string Kind { get; set; } = "manual_adjust";  // deposit_excess|refund_to_credit|loyalty_award|manual_adjust|redeem|redeem_reversal
        public string? ReferenceKind { get; set; }
        public Guid? ReferenceId { get; set; }
        public string? Note { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
