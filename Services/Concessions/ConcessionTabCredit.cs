using Services.Repositories.Data.ConcessionData;
using Services.Repositories.Interfaces;

namespace Services.Concessions
{
    /// <summary>
    /// Store credit as a tender on an open tab. A counter sale redeems once, under the sale id, and
    /// hands it back under the same id if the payment dies; the once-per-reference unique index on
    /// tenant_credit_entry (kind, reference_kind, reference_id) makes both idempotent. A tab is
    /// closed in ATTEMPTS (card declined on the reader, cashier switches to cash, retries), and a
    /// second redeem under the sale id would hit that index and report success WITHOUT debiting the
    /// balance, while the first attempt's reversal had already handed the money back: the sale would
    /// say "credit applied" and the customer would keep the credit. So every close attempt redeems
    /// under its own reference ('concession_tab_close', a fresh id stored on the sale as
    /// credit_redeem_ref): a retry reverses the earlier attempt by that id (exactly once) and redeems
    /// again under a new one (exactly once), and a void or a later refund reverses whichever attempt
    /// is outstanding by the id the sale carries. Legacy rows (a tab that redeemed before the attempt
    /// reference existed) still resolve to the sale-level reference.
    /// </summary>
    public static class ConcessionTabCredit
    {
        public const string AttemptReferenceKind = "concession_tab_close";
        public const string SaleReferenceKind = "concession_sale";

        /// <summary>
        /// The credit reference outstanding against this sale: the attempt reference when the tab
        /// redeemed under one, else the sale-level reference a counter sale (or a pre-attempt tab) used.
        /// </summary>
        public static (string Kind, Guid Id) ReferenceFor(ConcessionSale sale)
            => sale.CreditRedeemRef is { } attempt ? (AttemptReferenceKind, attempt) : (SaleReferenceKind, sale.Id);

        /// <summary>
        /// Hands back whatever an earlier attempt redeemed against this tab and clears the credit from
        /// the entity (the caller persists it). Idempotent per attempt: a second call for the same
        /// attempt is a no-op at the ledger. Returns false when there was nothing to hand back.
        /// </summary>
        public static async Task<bool> ReleaseAsync(ITenantCreditRepository credit, ConcessionSale sale, string note)
        {
            if (sale.CreditRedeemRef is null && sale.CreditAppliedCents <= 0) return false;
            var (kind, id) = ReferenceFor(sale);
            await credit.ReverseRedeem(sale.TenantId, kind, id, note);
            sale.CreditAppliedCents = 0;
            sale.CreditAccountId = null;
            sale.CreditRedeemRef = null;
            return true;
        }

        /// <summary>
        /// Redeems credit for ONE close attempt under a fresh attempt reference and records it on the
        /// entity (the caller persists it with the totals). False when the balance no longer covers it;
        /// the entity is left untouched then.
        /// </summary>
        public static async Task<bool> RedeemAsync(ITenantCreditRepository credit, ConcessionSale sale, Guid accountId, int cents, Guid? byUserId)
        {
            if (cents <= 0) return true;
            var attempt = Guid.NewGuid();
            if (!await credit.TryAdjust(accountId, sale.TenantId, -cents, "redeem", AttemptReferenceKind, attempt, null, byUserId))
                return false;
            sale.CreditAppliedCents = cents;
            sale.CreditAccountId = accountId;
            sale.CreditRedeemRef = attempt;
            return true;
        }
    }
}
