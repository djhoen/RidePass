using Services.Repositories.Data.CreditData;

namespace Services.Repositories.Interfaces
{
    public interface ITenantCreditRepository
    {
        Task<TenantCreditAccount?> GetAccount(Guid id, Guid tenantId);

        /// <summary>The rider's own account (self-view + online checkout); null when none exists.</summary>
        Task<TenantCreditAccount?> GetAccountForUser(Guid tenantId, Guid userId);

        /// <summary>Counter lookup: exact email or phone match (input normalized here).</summary>
        Task<TenantCreditAccount?> LookupAccount(Guid tenantId, string query);

        /// <summary>Admin search over email/phone/name; empty query lists newest accounts.</summary>
        Task<List<TenantCreditAccount>> SearchAccounts(Guid tenantId, string? query, int limit);

        /// <summary>
        /// Finds the customer's account (by user id, then email, then phone) or creates one.
        /// Fills in identity fields the existing account was missing. Returns null only when
        /// no identity at all was supplied.
        /// </summary>
        Task<TenantCreditAccount?> GetOrCreateAccount(Guid tenantId, Guid? userId, string? email, string? phone, string? displayName);

        Task<List<TenantCreditEntry>> ListEntries(Guid accountId, Guid tenantId, int limit);

        /// <summary>Whether an entry already exists for the given source (loyalty-award
        /// idempotency check, so a webhook + reconciler double-fire doesn't email twice).</summary>
        Task<bool> HasEntry(Guid tenantId, string kind, string referenceKind, Guid referenceId);

        /// <summary>Total credit outstanding for the tenant (their liability).</summary>
        Task<long> OutstandingTotal(Guid tenantId);

        /// <summary>
        /// Applies a signed delta atomically (balance floor-guarded at zero) and writes the
        /// entry. Returns false when the floor guard rejects it (insufficient balance).
        /// Reference-carrying kinds are once-per-source: a duplicate fire returns true.
        /// </summary>
        Task<bool> TryAdjust(Guid accountId, Guid tenantId, int deltaCents, string kind,
            string? referenceKind, Guid? referenceId, string? note, Guid? byUserId);

        /// <summary>
        /// Hands a sale's redeemed credit back (payment failed or sale refunded): reverses the
        /// 'redeem' entry recorded against the given source. Idempotent; no-op when nothing
        /// was redeemed there.
        /// </summary>
        Task ReverseRedeem(Guid tenantId, string referenceKind, Guid referenceId, string? note);

        // ── Multi-row checkout tender (gate counter / online event checkout) ──────
        /// <summary>Creates the tender anchor and debits the balance atomically; null when the
        /// balance raced away (caller proceeds without credit).</summary>
        Task<Guid?> TryCreateCheckoutTender(Guid tenantId, Guid accountId, int creditCents, string context);
        Task SetCheckoutTenderPaymentIntent(Guid tenderId, Guid tenantId, string paymentIntentId);
        Task<CheckoutCreditTender?> GetCheckoutTenderByPaymentIntentId(string paymentIntentId);
    }
}
