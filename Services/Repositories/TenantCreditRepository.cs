using Services.Helpers.Interfaces;
using Services.Repositories.Data.CreditData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantCreditRepository : ITenantCreditRepository
    {
        private readonly IDbHelper _db;
        public TenantCreditRepository(IDbHelper db) => _db = db;

        private const string AccountCols = @"
            id, tenant_id AS TenantId, user_id AS UserId, email, phone, display_name AS DisplayName,
            balance_cents AS BalanceCents, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string EntryCols = @"
            id, tenant_id AS TenantId, account_id AS AccountId, delta_cents AS DeltaCents, kind,
            reference_kind AS ReferenceKind, reference_id AS ReferenceId, note,
            created_by_user_id AS CreatedByUserId, created_at AS CreatedAt";

        // Identity normalization: emails compare lowercased, phones as digits only, so
        // "(555) 010-1234" and "5550101234" are the same account.
        private static string? NormEmail(string? email)
        {
            var e = email?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(e) ? null : e;
        }
        private static string? NormPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            return digits.Length >= 7 ? digits : null;
        }

        public async Task<TenantCreditAccount?> GetAccount(Guid id, Guid tenantId) =>
            (await _db.Query<TenantCreditAccount>(
                $"SELECT {AccountCols} FROM tenant_credit_account WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId })).FirstOrDefault();

        public async Task<TenantCreditAccount?> GetAccountForUser(Guid tenantId, Guid userId) =>
            (await _db.Query<TenantCreditAccount>(
                $"SELECT {AccountCols} FROM tenant_credit_account WHERE tenant_id = @tenantId AND user_id = @userId",
                new { tenantId, userId })).FirstOrDefault();

        public async Task<TenantCreditAccount?> LookupAccount(Guid tenantId, string query)
        {
            var email = NormEmail(query);
            var phone = NormPhone(query);
            return (await _db.Query<TenantCreditAccount>($@"
                SELECT {AccountCols} FROM tenant_credit_account
                WHERE tenant_id = @tenantId
                  AND ((@email IS NOT NULL AND lower(email) = @email)
                    OR (@phone IS NOT NULL AND phone = @phone))
                LIMIT 1", new { tenantId, email, phone })).FirstOrDefault();
        }

        public async Task<List<TenantCreditAccount>> SearchAccounts(Guid tenantId, string? query, int limit)
        {
            if (string.IsNullOrWhiteSpace(query))
                return (await _db.Query<TenantCreditAccount>($@"
                    SELECT {AccountCols} FROM tenant_credit_account
                    WHERE tenant_id = @tenantId ORDER BY updated_at DESC LIMIT @limit",
                    new { tenantId, limit })).ToList();

            var like = $"%{query.Trim()}%";
            var phone = NormPhone(query);
            return (await _db.Query<TenantCreditAccount>($@"
                SELECT {AccountCols} FROM tenant_credit_account
                WHERE tenant_id = @tenantId
                  AND (email ILIKE @like OR display_name ILIKE @like
                    OR (@phone IS NOT NULL AND phone LIKE '%' || @phone || '%'))
                ORDER BY updated_at DESC LIMIT @limit",
                new { tenantId, like, phone, limit })).ToList();
        }

        public async Task<TenantCreditAccount?> GetOrCreateAccount(
            Guid tenantId, Guid? userId, string? email, string? phone, string? displayName)
        {
            var e = NormEmail(email);
            var p = NormPhone(phone);
            var name = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            if (userId is null && e is null && p is null) return null;

            // Match by strongest identity first. On a hit, opportunistically fill fields the
            // account was missing so it accretes identity over time (a phone-only walk-in who
            // later gives an email becomes findable both ways).
            var existing = (await _db.Query<TenantCreditAccount>($@"
                SELECT {AccountCols} FROM tenant_credit_account
                WHERE tenant_id = @tenantId
                  AND ((@userId IS NOT NULL AND user_id = @userId)
                    OR (@e IS NOT NULL AND lower(email) = @e)
                    OR (@p IS NOT NULL AND phone = @p))
                ORDER BY (user_id = @userId) DESC NULLS LAST, (lower(email) = @e) DESC NULLS LAST
                LIMIT 1", new { tenantId, userId, e, p })).FirstOrDefault();
            if (existing is not null)
            {
                await _db.Execute(@"
                    UPDATE tenant_credit_account
                    SET user_id = COALESCE(user_id, @userId), email = COALESCE(email, @e),
                        phone = COALESCE(phone, @p), display_name = COALESCE(display_name, @name),
                        updated_at = now()
                    WHERE id = @id AND tenant_id = @tenantId",
                    new { id = existing.Id, tenantId, userId, e, p, name });
                return await GetAccount(existing.Id, tenantId);
            }

            try
            {
                var id = (await _db.Query<Guid>(@"
                    INSERT INTO tenant_credit_account (tenant_id, user_id, email, phone, display_name)
                    VALUES (@tenantId, @userId, @e, @p, @name) RETURNING id",
                    new { tenantId, userId, e, p, name })).First();
                return await GetAccount(id, tenantId);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                // Lost a create race on one of the identity uniques; the winner is our account.
                return (await _db.Query<TenantCreditAccount>($@"
                    SELECT {AccountCols} FROM tenant_credit_account
                    WHERE tenant_id = @tenantId
                      AND ((@userId IS NOT NULL AND user_id = @userId)
                        OR (@e IS NOT NULL AND lower(email) = @e)
                        OR (@p IS NOT NULL AND phone = @p))
                    LIMIT 1", new { tenantId, userId, e, p })).FirstOrDefault();
            }
        }

        public async Task<List<TenantCreditEntry>> ListEntries(Guid accountId, Guid tenantId, int limit) =>
            (await _db.Query<TenantCreditEntry>($@"
                SELECT {EntryCols} FROM tenant_credit_entry
                WHERE account_id = @accountId AND tenant_id = @tenantId
                ORDER BY created_at DESC LIMIT @limit",
                new { accountId, tenantId, limit })).ToList();

        public async Task<bool> HasEntry(Guid tenantId, string kind, string referenceKind, Guid referenceId) =>
            (await _db.Query<bool>(@"
                SELECT EXISTS (
                    SELECT 1 FROM tenant_credit_entry
                    WHERE tenant_id = @tenantId AND kind = @kind
                      AND reference_kind = @referenceKind AND reference_id = @referenceId)",
                new { tenantId, kind, referenceKind, referenceId })).First();

        public async Task<long> OutstandingTotal(Guid tenantId) =>
            (await _db.Query<long>(
                "SELECT COALESCE(SUM(balance_cents), 0) FROM tenant_credit_account WHERE tenant_id = @tenantId",
                new { tenantId })).First();

        public async Task<bool> TryAdjust(Guid accountId, Guid tenantId, int deltaCents, string kind,
            string? referenceKind, Guid? referenceId, string? note, Guid? byUserId)
        {
            if (deltaCents == 0) return true;
            try
            {
                // Balance update + entry in ONE statement so the floor guard and the ledger
                // can never disagree: if the UPDATE matches no row (insufficient balance or
                // wrong tenant), the INSERT selects nothing and we report failure.
                var n = await _db.Execute(@"
                    WITH upd AS (
                        UPDATE tenant_credit_account
                        SET balance_cents = balance_cents + @deltaCents, updated_at = now()
                        WHERE id = @accountId AND tenant_id = @tenantId AND balance_cents + @deltaCents >= 0
                        RETURNING id
                    )
                    INSERT INTO tenant_credit_entry
                        (tenant_id, account_id, delta_cents, kind, reference_kind, reference_id, note, created_by_user_id)
                    SELECT @tenantId, id, @deltaCents, @kind, @referenceKind, @referenceId, @note, @byUserId FROM upd",
                    new { accountId, tenantId, deltaCents, kind, referenceKind, referenceId, note, byUserId });
                return n > 0;
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                // Once-per-reference kind already recorded (webhook + reconciler, double-click):
                // the work is done, so report success.
                return true;
            }
        }

        private const string TenderCols = @"
            id, tenant_id AS TenantId, credit_account_id AS CreditAccountId,
            stripe_payment_intent_id AS StripePaymentIntentId, credit_applied_cents AS CreditAppliedCents,
            context, created_at AS CreatedAt";

        public async Task<Guid?> TryCreateCheckoutTender(Guid tenantId, Guid accountId, int creditCents, string context)
        {
            if (creditCents <= 0) return null;
            var id = Guid.NewGuid();
            await _db.Execute(@"
                INSERT INTO checkout_credit_tender (id, tenant_id, credit_account_id, credit_applied_cents, context)
                VALUES (@id, @tenantId, @accountId, @creditCents, @context)",
                new { id, tenantId, accountId, creditCents, context });
            // Debit after the anchor exists so the redeem entry can reference it. A raced balance
            // leaves an unreferenced tender row behind; harmless, but tidy it up.
            if (await TryAdjust(accountId, tenantId, -creditCents, "redeem", "credit_tender", id, null, null))
                return id;
            await _db.Execute("DELETE FROM checkout_credit_tender WHERE id = @id AND tenant_id = @tenantId",
                new { id, tenantId });
            return null;
        }

        public Task SetCheckoutTenderPaymentIntent(Guid tenderId, Guid tenantId, string paymentIntentId) => _db.Execute(@"
            UPDATE checkout_credit_tender SET stripe_payment_intent_id = @paymentIntentId
            WHERE id = @tenderId AND tenant_id = @tenantId", new { tenderId, tenantId, paymentIntentId });

        public async Task<CheckoutCreditTender?> GetCheckoutTenderByPaymentIntentId(string paymentIntentId) =>
            (await _db.Query<CheckoutCreditTender>(
                $"SELECT {TenderCols} FROM checkout_credit_tender WHERE stripe_payment_intent_id = @paymentIntentId LIMIT 1",
                new { paymentIntentId })).FirstOrDefault();

        public async Task ReverseRedeem(Guid tenantId, string referenceKind, Guid referenceId, string? note)
        {
            try
            {
                await _db.Execute(@"
                    WITH r AS (
                        SELECT e.account_id, e.delta_cents
                        FROM tenant_credit_entry e
                        WHERE e.tenant_id = @tenantId AND e.kind = 'redeem'
                          AND e.reference_kind = @referenceKind AND e.reference_id = @referenceId
                    ),
                    upd AS (
                        UPDATE tenant_credit_account a
                        SET balance_cents = a.balance_cents - r.delta_cents, updated_at = now()
                        FROM r
                        WHERE a.id = r.account_id AND a.tenant_id = @tenantId
                        RETURNING a.id, r.delta_cents
                    )
                    INSERT INTO tenant_credit_entry
                        (tenant_id, account_id, delta_cents, kind, reference_kind, reference_id, note)
                    SELECT @tenantId, id, -delta_cents, 'redeem_reversal', @referenceKind, @referenceId, @note FROM upd",
                    new { tenantId, referenceKind, referenceId, note });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                // Already reversed; nothing to do.
            }
        }
    }
}
