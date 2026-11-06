using NUnit.Framework;
using Services.Concessions;
using Services.Repositories.Data.ConcessionData;
using Services.Repositories.Data.CreditData;
using Services.Repositories.Interfaces;

namespace UnitTests
{
    // Store credit on an F&B tab is redeemed once per CLOSE ATTEMPT. These tests drive the exact
    // sequence that used to lose money (redeem -> card declined -> retry by cash -> paid) against an
    // in-memory credit ledger that behaves like the real one: a balance with a floor at zero, and the
    // once-per-reference unique index that makes a duplicate redeem report success WITHOUT debiting.
    [TestFixture]
    public class ConcessionTabCreditTests
    {
        private static readonly Guid Tenant = Guid.NewGuid();

        private static ConcessionSale Tab(int totalCents) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Status = "open",
            TotalCents = totalCents,
            TabOpenedAt = DateTime.UtcNow,
        };

        [Test]
        public async Task Retry_RedeemDeclinedCardThenCash_MovesTheBalanceExactlyOnce()
        {
            var credit = new FakeCreditLedger(Tenant, balanceCents: 1_000);
            var sale = Tab(800);

            // Attempt 1 (card): credit redeemed, PaymentIntent handed to the reader, card declines.
            // The server never hears about the decline; the row keeps attempt 1's credit.
            Assert.That(await ConcessionTabCredit.RedeemAsync(credit, sale, credit.AccountId, 800, null), Is.True);
            var attempt1 = sale.CreditRedeemRef;
            Assert.That(attempt1, Is.Not.Null);
            Assert.That(credit.Balance, Is.EqualTo(200));

            // Attempt 2 (cash): what CloseTab does first is hand attempt 1 back, then redeem again.
            Assert.That(await ConcessionTabCredit.ReleaseAsync(credit, sale, "tab close retried"), Is.True);
            Assert.That(credit.Balance, Is.EqualTo(1_000), "attempt 1 reversed exactly once");
            Assert.That(sale.CreditAppliedCents, Is.EqualTo(0));
            Assert.That(sale.CreditRedeemRef, Is.Null);

            Assert.That(await ConcessionTabCredit.RedeemAsync(credit, sale, credit.AccountId, 800, null), Is.True);
            Assert.That(sale.CreditRedeemRef, Is.Not.EqualTo(attempt1), "each attempt has its own reference");
            Assert.That(credit.Balance, Is.EqualTo(200), "the retry debited for real: this is the bug the sale-level reference had");

            // Paid. The customer paid 800 of credit once, and the ledger says exactly that.
            Assert.That(credit.Entries.Count(e => e.Kind == "redeem"), Is.EqualTo(2));
            Assert.That(credit.Entries.Count(e => e.Kind == "redeem_reversal"), Is.EqualTo(1));
            Assert.That(credit.Entries.Sum(e => e.DeltaCents), Is.EqualTo(-800));
        }

        [Test]
        public async Task TheOldSaleLevelReference_WouldHaveReportedSuccessWithoutDebiting()
        {
            // Documents WHY attempts carry their own reference: the real repository swallows the
            // unique violation and returns true, so a retry under the same reference left the row
            // claiming credit the balance never lost.
            var credit = new FakeCreditLedger(Tenant, balanceCents: 1_000);
            var saleId = Guid.NewGuid();
            Assert.That(await credit.TryAdjust(credit.AccountId, Tenant, -800, "redeem", "concession_sale", saleId, null, null), Is.True);
            await credit.ReverseRedeem(Tenant, "concession_sale", saleId, "retry");
            Assert.That(credit.Balance, Is.EqualTo(1_000));
            Assert.That(await credit.TryAdjust(credit.AccountId, Tenant, -800, "redeem", "concession_sale", saleId, null, null), Is.True,
                "reports success...");
            Assert.That(credit.Balance, Is.EqualTo(1_000), "...but the balance did not move");
        }

        [Test]
        public async Task Void_AfterAFailedAttempt_HandsCreditBackOnce_EvenIfVoidRunsTwice()
        {
            var credit = new FakeCreditLedger(Tenant, balanceCents: 500);
            var sale = Tab(300);
            Assert.That(await ConcessionTabCredit.RedeemAsync(credit, sale, credit.AccountId, 300, null), Is.True);
            Assert.That(credit.Balance, Is.EqualTo(200));

            // VoidTab reverses by the attempt reference; a second void (or a retry racing it) is a no-op.
            var persisted = Snapshot(sale);
            Assert.That(await ConcessionTabCredit.ReleaseAsync(credit, persisted, "tab voided"), Is.True);
            Assert.That(credit.Balance, Is.EqualTo(500));
            Assert.That(await ConcessionTabCredit.ReleaseAsync(credit, Snapshot(sale), "tab voided"), Is.True);
            Assert.That(credit.Balance, Is.EqualTo(500), "the once-per-reference index makes the second reversal a no-op");
            Assert.That(credit.Entries.Count(e => e.Kind == "redeem_reversal"), Is.EqualTo(1));
        }

        [Test]
        public async Task Refund_OfAPaidTab_ReversesTheAttemptTheSaleCarries()
        {
            var credit = new FakeCreditLedger(Tenant, balanceCents: 1_000);
            var sale = Tab(800);
            Assert.That(await ConcessionTabCredit.RedeemAsync(credit, sale, credit.AccountId, 250, null), Is.True);
            sale.Status = "paid";

            // RefundSale resolves the reference from the row, not from the sale id.
            var (kind, id) = ConcessionTabCredit.ReferenceFor(sale);
            Assert.That(kind, Is.EqualTo(ConcessionTabCredit.AttemptReferenceKind));
            Assert.That(id, Is.EqualTo(sale.CreditRedeemRef));
            await credit.ReverseRedeem(Tenant, kind, id, "sale refunded");
            Assert.That(credit.Balance, Is.EqualTo(1_000));
            await credit.ReverseRedeem(Tenant, kind, id, "sale refunded");
            Assert.That(credit.Balance, Is.EqualTo(1_000), "double refund click is idempotent");
        }

        [Test]
        public void ReferenceFor_FallsBackToTheSaleId_ForCounterSalesAndLegacyTabs()
        {
            var counterSale = new ConcessionSale { Id = Guid.NewGuid(), TenantId = Tenant, CreditAppliedCents = 100 };
            Assert.That(ConcessionTabCredit.ReferenceFor(counterSale), Is.EqualTo((ConcessionTabCredit.SaleReferenceKind, counterSale.Id)));
        }

        [Test]
        public async Task Redeem_InsufficientBalance_LeavesTheSaleUntouched()
        {
            var credit = new FakeCreditLedger(Tenant, balanceCents: 100);
            var sale = Tab(800);
            Assert.That(await ConcessionTabCredit.RedeemAsync(credit, sale, credit.AccountId, 800, null), Is.False);
            Assert.That(sale.CreditAppliedCents, Is.EqualTo(0));
            Assert.That(sale.CreditRedeemRef, Is.Null);
            Assert.That(credit.Balance, Is.EqualTo(100));
        }

        [Test]
        public async Task Release_WithNothingOutstanding_IsANoOp()
        {
            var credit = new FakeCreditLedger(Tenant, balanceCents: 100);
            Assert.That(await ConcessionTabCredit.ReleaseAsync(credit, Tab(500), "nothing"), Is.False);
            Assert.That(credit.Entries, Is.Empty);
        }

        // What another register (or the void) sees: the row as persisted, not the in-memory entity the
        // first caller already cleared.
        private static ConcessionSale Snapshot(ConcessionSale s) => new()
        {
            Id = s.Id, TenantId = s.TenantId, Status = s.Status, TotalCents = s.TotalCents, TabOpenedAt = s.TabOpenedAt,
            CreditAppliedCents = s.CreditAppliedCents, CreditAccountId = s.CreditAccountId, CreditRedeemRef = s.CreditRedeemRef,
        };

        /// <summary>
        /// One account, one ledger, the two invariants of the real TenantCreditRepository that matter
        /// here: the balance never goes below zero, and (kind, reference_kind, reference_id) is unique
        /// for reference-carrying kinds, with the duplicate swallowed exactly as the repository does
        /// (TryAdjust returns true; ReverseRedeem returns quietly) and NO balance change on a duplicate,
        /// because the real statement writes balance and entry atomically.
        /// </summary>
        private sealed class FakeCreditLedger : ITenantCreditRepository
        {
            public Guid AccountId { get; } = Guid.NewGuid();
            public int Balance { get; private set; }
            public List<TenantCreditEntry> Entries { get; } = new();
            private readonly Guid _tenantId;

            public FakeCreditLedger(Guid tenantId, int balanceCents) { _tenantId = tenantId; Balance = balanceCents; }

            private bool Duplicate(string kind, string? refKind, Guid? refId) =>
                refId is not null && kind is "deposit_excess" or "refund_to_credit" or "redeem" or "redeem_reversal" or "loyalty_award"
                && Entries.Any(e => e.Kind == kind && e.ReferenceKind == refKind && e.ReferenceId == refId);

            public Task<bool> TryAdjust(Guid accountId, Guid tenantId, int deltaCents, string kind, string? referenceKind, Guid? referenceId, string? note, Guid? byUserId)
            {
                if (deltaCents == 0) return Task.FromResult(true);
                if (accountId != AccountId || tenantId != _tenantId || Balance + deltaCents < 0) return Task.FromResult(false);
                if (Duplicate(kind, referenceKind, referenceId)) return Task.FromResult(true);   // 23505 swallowed, nothing written
                Balance += deltaCents;
                Entries.Add(new TenantCreditEntry { Kind = kind, DeltaCents = deltaCents, ReferenceKind = referenceKind, ReferenceId = referenceId, AccountId = accountId, TenantId = tenantId });
                return Task.FromResult(true);
            }

            public Task ReverseRedeem(Guid tenantId, string referenceKind, Guid referenceId, string? note)
            {
                var redeem = Entries.FirstOrDefault(e => e.Kind == "redeem" && e.ReferenceKind == referenceKind && e.ReferenceId == referenceId);
                if (redeem is null || Duplicate("redeem_reversal", referenceKind, referenceId)) return Task.CompletedTask;
                Balance -= redeem.DeltaCents;
                Entries.Add(new TenantCreditEntry { Kind = "redeem_reversal", DeltaCents = -redeem.DeltaCents, ReferenceKind = referenceKind, ReferenceId = referenceId, AccountId = AccountId, TenantId = tenantId });
                return Task.CompletedTask;
            }

            public Task<TenantCreditAccount?> GetAccount(Guid id, Guid tenantId) =>
                Task.FromResult<TenantCreditAccount?>(id == AccountId ? new TenantCreditAccount { Id = AccountId, TenantId = tenantId, BalanceCents = Balance } : null);
            public Task<bool> HasEntry(Guid tenantId, string kind, string referenceKind, Guid referenceId) =>
                Task.FromResult(Entries.Any(e => e.Kind == kind && e.ReferenceKind == referenceKind && e.ReferenceId == referenceId));
            public Task<List<TenantCreditEntry>> ListEntries(Guid accountId, Guid tenantId, int limit) => Task.FromResult(Entries.ToList());
            public Task<long> OutstandingTotal(Guid tenantId) => Task.FromResult((long)Balance);

            // Not exercised by the tab rules.
            public Task<TenantCreditAccount?> GetAccountForUser(Guid tenantId, Guid userId) => throw new NotSupportedException();
            public Task<TenantCreditAccount?> LookupAccount(Guid tenantId, string query) => throw new NotSupportedException();
            public Task<List<TenantCreditAccount>> SearchAccounts(Guid tenantId, string? query, int limit) => throw new NotSupportedException();
            public Task<TenantCreditAccount?> GetOrCreateAccount(Guid tenantId, Guid? userId, string? email, string? phone, string? displayName) => throw new NotSupportedException();
            public Task<Guid?> TryCreateCheckoutTender(Guid tenantId, Guid accountId, int creditCents, string context) => throw new NotSupportedException();
            public Task SetCheckoutTenderPaymentIntent(Guid tenderId, Guid tenantId, string paymentIntentId) => throw new NotSupportedException();
            public Task<CheckoutCreditTender?> GetCheckoutTenderByPaymentIntentId(string paymentIntentId) => throw new NotSupportedException();
        }
    }
}
