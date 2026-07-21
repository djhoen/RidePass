using NUnit.Framework;
using Services.Accounting;
using Services.Repositories.Data.QuickBooksData;

namespace UnitTests
{
    // Covers the journal the QuickBooks sync posts into a customer's real books. Two invariants
    // dominate every test here:
    //
    //   1. DEBITS == CREDITS, always. Build() throws rather than return an unbalanced draft, so
    //      several of these assert the totals explicitly as a tripwire on the accumulator math.
    //   2. Tax, tips and gift cards are NEVER revenue. Tax is remitted to a jurisdiction, tips are
    //      owed to staff, and a gift card is revenue exactly once (at redemption, not at sale), //      booking any of them as income is the failure mode that would actually mislead an
    //      accountant.
    [TestFixture]
    public class JournalEntryBuilderTests
    {
        private static readonly DateOnly Day = new(2026, 7, 16);

        private static AccountingEntry Sale(
            string sourceKind = "event_ticket",
            int gross = 0, int fee = 0, int cut = 0, int net = 0,
            int tax = 0, int tip = 0, int giftCard = 0,
            string paymentMethod = "stripe", string entryKind = "sale") =>
            new()
            {
                TenantId = Guid.NewGuid(),
                EntryKind = entryKind,
                SourceKind = sourceKind,
                SourceId = Guid.NewGuid(),
                OccurredAtUtc = new DateTime(2026, 7, 16, 18, 0, 0, DateTimeKind.Utc),
                BusinessDate = Day,
                PaymentMethod = paymentMethod,
                GrossCents = gross,
                StripeFeeCents = fee,
                RidepassCutCents = cut,
                NetToTenantCents = net,
                TaxCents = tax,
                TipCents = tip,
                GiftCardAppliedCents = giftCard,
            };

        private static int Signed(JournalDraft d, string key)
        {
            var line = d.Lines.SingleOrDefault(l => l.AccountKey == key);
            if (line == default) return 0;
            return line.IsDebit ? line.AmountCents : -line.AmountCents;
        }

        private static void AssertBalanced(JournalDraft d)
        {
            Assert.That(d.TotalDebitCents, Is.EqualTo(d.TotalCreditCents),
                $"unbalanced: {string.Join(" | ", d.Lines.Select(l => l.Describe()))}");
        }

        // ── The core identity ────────────────────────────────────────────────────────────

        [Test]
        public void PlatformStripeSale_BooksRevenueFeesAndReceivable()
        {
            // $44.00 ticket, $1.57 Stripe fee, $4.00 our cut, $38.43 net, a real row from the ledger.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(gross: 4400, fee: 157, cut: 400, net: 3843) }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(-4400), "revenue is credited gross");
            Assert.That(Signed(d, QboAccountKeys.ExpenseStripeFees), Is.EqualTo(157));
            Assert.That(Signed(d, QboAccountKeys.ExpenseRidepassFees), Is.EqualTo(400));
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(3843),
                "the tenant is owed net, and the two fee expenses make the debits sum to gross");
        }

        [Test]
        public void TaxAndTips_AreLiabilities_NotRevenue()
        {
            // $50 concession sale: $4 tax collected for the jurisdiction, $5 tip for staff.
            // Revenue must be the $41 remainder, not the $50 the customer handed over.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "concession", gross: 5000, fee: 175, cut: 150, net: 4675, tax: 400, tip: 500) },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueConcession), Is.EqualTo(-4100), "gross - tax - tip");
            Assert.That(Signed(d, QboAccountKeys.LiabilitySalesTax), Is.EqualTo(-400));
            Assert.That(Signed(d, QboAccountKeys.LiabilityTips), Is.EqualTo(-500));
        }

        [Test]
        public void CashSale_DebitsCashAndLeavesUsOwedOurCut()
        {
            // Cash: the tenant physically holds the money, so the ledger books net = -cut, they owe
            // RidePass its cut out of the next payout. The receivable must go NEGATIVE (a credit).
            var d = JournalEntryBuilder.Build(
                new[] { Sale(gross: 2000, fee: 0, cut: 60, net: -60, paymentMethod: "cash") },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.AssetUndepositedCash), Is.EqualTo(2000));
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(-2000));
            Assert.That(Signed(d, QboAccountKeys.ExpenseRidepassFees), Is.EqualTo(60));
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(-60),
                "cash sale leaves RidePass owed its cut, so the receivable is a credit");
        }

        // ── Gift cards: the double-count trap ────────────────────────────────────────────

        [Test]
        public void GiftCardFullyCoveredSale_DrawsDownLiability_DoesNotTouchCash()
        {
            // $100 redeemed against a gift card. No card charge happens now, it happened when the
            // card was bought. Revenue is earned NOW; the liability created at sale is discharged.
            // The receivable goes -$3 because the platform holds the float and keeps its $3 cut.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(gross: 10000, fee: 0, cut: 300, net: 9700, giftCard: 10000, paymentMethod: "voucher") },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.LiabilityGiftCard), Is.EqualTo(10000), "liability discharged");
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(-10000), "revenue recognised at redemption");
            Assert.That(Signed(d, QboAccountKeys.ExpenseRidepassFees), Is.EqualTo(300));
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(-300));
            Assert.That(Signed(d, QboAccountKeys.AssetUndepositedCash), Is.EqualTo(0), "no cash moved at redemption");
        }

        [Test]
        public void PartiallyGiftCardFundedSale_SplitsTenderAndStillBalances()
        {
            // $100 sale, $20 off a gift card, $80 on a card. This is the case that breaks naive
            // implementations: the gift card term has to come out of the receivable, not be added on
            // top, or the day over-credits by $20.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(gross: 10000, fee: 300, cut: 300, net: 9400, giftCard: 2000) },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.LiabilityGiftCard), Is.EqualTo(2000));
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(-10000));
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(7400), "net 9400 less the 2000 gift card");
            Assert.That(d.TotalDebitCents, Is.EqualTo(10000));
        }

        // ── Refunds fall out of the sign, with no special case ───────────────────────────

        [Test]
        public void FullRefund_ExactlyReversesTheSale()
        {
            var sale = Sale(gross: 4400, fee: 157, cut: 400, net: 3843, tax: 200);
            // RefundLedgerMath's plain-Stripe branch: gross -4400, cut not clawed back, net -4400.
            // The view prorates tax to -200 off the same source row.
            var refund = Sale(gross: -4400, fee: 0, cut: 0, net: -4400, tax: -200, entryKind: "refund");

            var d = JournalEntryBuilder.Build(new[] { sale, refund }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(0), "revenue nets to zero");
            Assert.That(Signed(d, QboAccountKeys.LiabilitySalesTax), Is.EqualTo(0), "tax liability nets to zero");
            Assert.That(Signed(d, QboAccountKeys.ExpenseRidepassFees), Is.EqualTo(400),
                "a tenant-initiated refund intentionally leaves our prior cut in place");
        }

        [Test]
        public void ZeroingAccounts_AreDroppedFromTheDraft()
        {
            // A same-day sale and full refund of the same ticket must not emit DR 0.00 / CR 0.00
            // lines. QBO rejects zero-amount lines.
            var sale = Sale(gross: 1000, fee: 0, cut: 0, net: 1000, paymentMethod: "cash");
            var refund = Sale(gross: -1000, fee: 0, cut: 0, net: -1000, paymentMethod: "cash", entryKind: "refund");

            var d = JournalEntryBuilder.Build(new[] { sale, refund }, Day);

            Assert.That(d.Lines, Is.Empty);
            Assert.That(d.IsEmpty, Is.True);
        }

        // ── Direct charge mode: a different set of books ─────────────────────────────────

        [Test]
        public void DirectChargeSale_BooksToStripeClearing_AndOmitsUnknownStripeFee()
        {
            // Direct mode: the tenant is merchant of record. Money lands in THEIR Stripe balance and
            // THEY bore the Stripe fee, which our ledger records as 0 because we genuinely don't
            // know it. We must book only what we know, and must not invent a fee.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(gross: 10000, fee: 0, cut: 300, net: 0, paymentMethod: "stripe_direct") },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(-10000));
            Assert.That(Signed(d, QboAccountKeys.ExpenseRidepassFees), Is.EqualTo(300), "our application fee");
            Assert.That(Signed(d, QboAccountKeys.AssetStripeClearing), Is.EqualTo(9700), "gross less our app fee");
            Assert.That(Signed(d, QboAccountKeys.ExpenseStripeFees), Is.EqualTo(0), "we don't know the tenant's own fee");
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(0), "no platform settlement in direct mode");
        }

        [Test]
        public void DirectChargeTenantTakingCash_StillUsesTheReceivable_NotStripeClearing()
        {
            // A direct-charge tenant still takes cash at the counter, and cash never touches Stripe, // so the row is payment_method='cash' (not 'stripe_direct') and books net = -cut against
            // the RidePass receivable, exactly like a platform tenant's cash sale. This is why
            // QuickBooksController.RequiredKeys demands the receivable for EVERY tenant: gating it on
            // charge mode left a direct tenant's first cash sale unable to post.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(gross: 2000, fee: 0, cut: 60, net: -60, paymentMethod: "cash") }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(-60));
            Assert.That(Signed(d, QboAccountKeys.AssetStripeClearing), Is.EqualTo(0), "cash never lands in Stripe");
        }

        [Test]
        public void ChargeModeIsReadPerEntry_SoAModeFlipCannotRewriteHistory()
        {
            // A super admin can flip stripe_charge_mode. Old rows keep the payment_method snapshotted
            // at charge time, and the builder must honour each row's own tender rather than the
            // tenant's current mode, otherwise re-syncing an old day would re-book it under the new
            // mode. Both kinds in one day must coexist and balance.
            var platformEra = Sale(gross: 10000, fee: 300, cut: 300, net: 9400, paymentMethod: "stripe");
            var directEra = Sale(gross: 10000, fee: 0, cut: 300, net: 0, paymentMethod: "stripe_direct");

            var d = JournalEntryBuilder.Build(new[] { platformEra, directEra }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(9400), "the platform-era row only");
            Assert.That(Signed(d, QboAccountKeys.AssetStripeClearing), Is.EqualTo(9700), "the direct-era row only");
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(-20000));
        }

        // ── Platform charges are an expense, not negative revenue ────────────────────────

        [Test]
        public void SmsAndEmailCharges_BookAsExpense_NotNegativeRevenue()
        {
            // These carry a negative gross and a source_kind that points at a billing artifact, not a
            // sale. Running them through the revenue path would DEBIT revenue, reporting the SMS
            // bill as a reduction in ticket sales.
            var sms = Sale(sourceKind: "tenant_billing_event", gross: -200, net: -200, entryKind: "sms_charge");
            var email = Sale(sourceKind: "email_campaign", gross: -1500, net: -1500, entryKind: "email_charge");

            var d = JournalEntryBuilder.Build(new[] { sms, email }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.ExpenseRidepassFees), Is.EqualTo(1700));
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(-1700), "netted out of the payout");
            Assert.That(Signed(d, QboAccountKeys.RevenueOther), Is.EqualTo(0), "never touches revenue");
        }

        [Test]
        public void DisputeFee_BooksFeeAgainstReceivable_WithNoRevenueEffect()
        {
            // gross = 0, the $15 sits in stripe_fee, net = -1500. Falls out of the general formula
            // with no special case: the only two lines are the fee and the receivable.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(gross: 0, fee: 1500, cut: 0, net: -1500, entryKind: "dispute_fee") },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.ExpenseStripeFees), Is.EqualTo(1500));
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(-1500));
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(0));
        }

        // ── Rental deposits: a liability, not income ─────────────────────────────────────

        [Test]
        public void DepositCollected_IsALiability_NotRevenue()
        {
            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "rental", gross: 20000, entryKind: "deposit_collected") },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(20000));
            Assert.That(Signed(d, QboAccountKeys.LiabilityRentalDeposit), Is.EqualTo(-20000));
            Assert.That(Signed(d, QboAccountKeys.RevenueRental), Is.EqualTo(0), "a refundable deposit is not earned");
        }

        [Test]
        public void DepositSettlement_KeepsDamageAsIncome_AndReturnsTheRest()
        {
            // $200 deposit, $50 kept for damage, $150 back to the rider. Three pieces have to compose:
            // the view emits collected + released (the hold lifecycle), and RentalController writes the
            // $50 damage into the ledger as a real 'rental_deposit' sale.
            var collected = Sale(sourceKind: "rental", gross: 20000, entryKind: "deposit_collected");
            var released  = Sale(sourceKind: "rental", gross: 20000, entryKind: "deposit_released");
            var damage    = Sale(sourceKind: "rental_deposit", gross: 5000, fee: 0, cut: 0, net: 5000);

            var d = JournalEntryBuilder.Build(new[] { collected, released, damage }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.LiabilityRentalDeposit), Is.EqualTo(0), "the hold is fully unwound");
            Assert.That(Signed(d, QboAccountKeys.RevenueDepositForfeited), Is.EqualTo(-5000), "damage kept is income");
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(5000),
                "200 in, 200 released, 50 earned back: only the damage stays owed to the track");
        }

        [Test]
        public void FullyRefundedDeposit_LeavesNothingBehind()
        {
            // Undamaged return: the whole deposit goes back and no 'rental_deposit' sale is written.
            // Every account must net to zero, so the day emits no lines at all for it.
            var collected = Sale(sourceKind: "rental", gross: 20000, entryKind: "deposit_collected");
            var released  = Sale(sourceKind: "rental", gross: 20000, entryKind: "deposit_released");

            var d = JournalEntryBuilder.Build(new[] { collected, released }, Day);

            Assert.That(d.Lines, Is.Empty);
            Assert.That(Signed(d, QboAccountKeys.RevenueDepositForfeited), Is.EqualTo(0), "no damage, no income");
        }

        [Test]
        public void DepositReleased_DoesNotBookIncomeItself()
        {
            // The release row only unwinds the hold. If it also credited forfeited-deposit revenue,
            // the damage would be counted twice: once here and once from the ledger's sale entry.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "rental", gross: 20000, entryKind: "deposit_released") }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueDepositForfeited), Is.EqualTo(0));
            Assert.That(Signed(d, QboAccountKeys.LiabilityRentalDeposit), Is.EqualTo(20000));
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(-20000));
        }

        [Test]
        public void CapturedDamage_OnADirectChargeTenant_BooksIncomeWithNoPlatformSettlement()
        {
            // Direct mode: the deposit landed in the tenant's own Stripe account, so they already hold
            // the damage money. Income is still recognised, but the platform owes them nothing for it.
            var collected = Sale(sourceKind: "rental", gross: 20000, entryKind: "deposit_collected", paymentMethod: "stripe_direct");
            var released  = Sale(sourceKind: "rental", gross: 20000, entryKind: "deposit_released", paymentMethod: "stripe_direct");
            var damage    = Sale(sourceKind: "rental_deposit", gross: 5000, fee: 0, cut: 0, net: 0, paymentMethod: "stripe_direct");

            var d = JournalEntryBuilder.Build(new[] { collected, released, damage }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueDepositForfeited), Is.EqualTo(-5000));
            Assert.That(Signed(d, QboAccountKeys.AssetStripeClearing), Is.EqualTo(5000), "the damage stays in their own balance");
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(0), "no platform settlement in direct mode");
        }

        [Test]
        public void HeldDepositCapture_CarriesItsOwnStripeFee_AsRevenueLessFee()
        {
            // A short-rental deposit is a hold; capturing damage is a FRESH charge on the deposit PI,
            // so it has its own Stripe fee (unlike the charged-deposit path, where the fee rode the
            // rental). $50 kept, $1.75 fee: the track's forfeited-deposit income is the full $50, and
            // the processing fee is an expense — they net $48.25.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "rental_deposit", gross: 5000, fee: 175, cut: 0, net: 4825) }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueDepositForfeited), Is.EqualTo(-5000), "damage income is the gross kept");
            Assert.That(Signed(d, QboAccountKeys.ExpenseStripeFees), Is.EqualTo(175), "the capture's own processing fee");
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(4825), "owed to the track, net of the fee");
            Assert.That(Signed(d, QboAccountKeys.ExpenseRidepassFees), Is.EqualTo(0), "no RidePass cut on a deposit");
        }

        [Test]
        public void CapturedDamage_TakesNoRidePassCut()
        {
            // We charge a service fee on the rental fee, never on the deposit, and keeping a deposit
            // for damage doesn't change what we're owed.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "rental_deposit", gross: 5000, fee: 0, cut: 0, net: 5000) }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.ExpenseRidepassFees), Is.EqualTo(0));
            Assert.That(Signed(d, QboAccountKeys.RevenueDepositForfeited), Is.EqualTo(-5000));
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(5000));
        }

        // ── Aggregation ──────────────────────────────────────────────────────────────────

        [Test]
        public void ManyEntries_SummariseIntoOneBalancedEntryPerAccount()
        {
            var entries = Enumerable.Range(0, 250)
                .Select(_ => Sale(gross: 4400, fee: 157, cut: 400, net: 3843, tax: 200))
                .Concat(Enumerable.Range(0, 40).Select(_ =>
                    Sale(sourceKind: "concession", gross: 1250, fee: 66, cut: 38, net: 1146, tax: 100, tip: 150)))
                .ToList();

            var d = JournalEntryBuilder.Build(entries, Day);

            AssertBalanced(d);
            Assert.That(d.EntryCount, Is.EqualTo(290));
            Assert.That(d.Lines.Select(l => l.AccountKey), Is.Unique, "one line per account, not per sale");
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(-(4400 - 200) * 250));
            Assert.That(Signed(d, QboAccountKeys.LiabilityTips), Is.EqualTo(-150 * 40));
            Assert.That(d.TotalDebitCents, Is.EqualTo(4400 * 250 + 1250 * 40));
        }

        [Test]
        public void UnknownSourceKind_FallsBackToRevenueOther_RatherThanFailingTheDay()
        {
            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "some_future_kind", gross: 1000, net: 1000) },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueOther), Is.EqualTo(-1000));
        }
    }
}
