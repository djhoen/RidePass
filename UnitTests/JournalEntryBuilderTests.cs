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
            string paymentMethod = "stripe", string entryKind = "sale",
            string? revenueKeyOverride = null) =>
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
                RevenueKeyOverride = revenueKeyOverride,
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

        // ── Gift card SALES (Script0273 Part 3) ────────────────────────────────────────
        // The credit that was missing for as long as the sync has existed. Selling a card takes
        // money and creates an obligation; it earns nothing. The revenue arrives later, when the
        // card is spent, and the redemption draws this same liability back down.

        private static AccountingEntry GiftCardSale(int gross, string paymentMethod = "stripe") =>
            Sale(sourceKind: "gift_card", gross: gross, net: gross,
                 paymentMethod: paymentMethod, entryKind: "gift_card_sold");

        [Test]
        public void GiftCardSale_Platform_CreditsLiabilityAndDebitsReceivable_NeverRevenue()
        {
            // A $100 card bought online from a platform-charge track. RidePass holds the float.
            var d = JournalEntryBuilder.Build(new[] { GiftCardSale(10000) }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.LiabilityGiftCard), Is.EqualTo(-10000),
                "selling a card creates the obligation it is later redeemed against");
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(10000));
            Assert.That(d.Lines.Any(l => l.AccountKey.StartsWith("revenue_")), Is.False,
                "a gift card sale earns nothing until it is spent");
            Assert.That(Signed(d, QboAccountKeys.LiabilitySalesTax), Is.EqualTo(0),
                "selling stored value is not a taxable sale");
        }

        [Test]
        public void GiftCardSale_DirectCharge_DebitsStripeClearing()
        {
            // Direct charge: the card was sold on the track's own connected account, so the float
            // is in THEIR Stripe balance, not in a RidePass receivable.
            var d = JournalEntryBuilder.Build(
                new[] { GiftCardSale(5000, paymentMethod: "stripe_direct") }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.AssetStripeClearing), Is.EqualTo(5000));
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(0));
            Assert.That(Signed(d, QboAccountKeys.LiabilityGiftCard), Is.EqualTo(-5000));
        }

        [Test]
        public void GiftCardSale_Cash_DebitsUndepositedCash()
        {
            var d = JournalEntryBuilder.Build(
                new[] { GiftCardSale(2500, paymentMethod: "cash") }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.AssetUndepositedCash), Is.EqualTo(2500));
            Assert.That(Signed(d, QboAccountKeys.LiabilityGiftCard), Is.EqualTo(-2500));
        }

        [Test]
        public void GiftCardSoldAndFullyRedeemedSameDay_NetsLiabilityToZero_ReceivableToNetOfCut()
        {
            // The worked example from the JournalEntryBuilder header. A $100 card is bought and
            // then spent in full on a $100 ticket carrying a $4 RidePass cut. The two halves of
            // the liability cancel and the track is left owed exactly its payout.
            var d = JournalEntryBuilder.Build(
                new[]
                {
                    GiftCardSale(10000),
                    Sale(gross: 10000, fee: 0, cut: 400, net: 9600, giftCard: 10000, paymentMethod: "voucher"),
                },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.LiabilityGiftCard), Is.EqualTo(0),
                "sold then fully spent: the obligation is discharged");
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(9600),
                "100 owed at sale, less the 4 cut at redemption");
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(-10000),
                "the revenue lands once, at redemption");
            Assert.That(Signed(d, QboAccountKeys.ExpenseRidepassFees), Is.EqualTo(400));
        }

        [Test]
        public void GiftCardSoldAndPartlyRedeemed_LeavesTheRemainderOutstanding()
        {
            // $100 card, $30 of it spent on a concession sale that was otherwise paid by card.
            var d = JournalEntryBuilder.Build(
                new[]
                {
                    GiftCardSale(10000),
                    Sale(sourceKind: "concession", gross: 5000, fee: 90, cut: 150, net: 4760,
                         tax: 400, giftCard: 3000),
                },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.LiabilityGiftCard), Is.EqualTo(-7000),
                "the unspent 70 stays on the balance sheet as an obligation");
            Assert.That(Signed(d, QboAccountKeys.RevenueConcession), Is.EqualTo(-4600),
                "tax is still carved out of the gross even when a card funded part of it");
        }

        // ── Bike shop (Script0273 Part 1 CASE) ──────────────────────────────────────────

        [Test]
        public void ShopSale_BooksBikeShopRevenue_WithTaxCarvedOut()
        {
            // $120 counter sale, $7.20 tax. Before Script0273 this fell through to revenue_other
            // with the tax silently booked as income.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "shop_sale", gross: 12000, fee: 378, cut: 360, net: 11262, tax: 720) },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueBikeShop), Is.EqualTo(-11280));
            Assert.That(Signed(d, QboAccountKeys.LiabilitySalesTax), Is.EqualTo(-720));
            Assert.That(Signed(d, QboAccountKeys.RevenueOther), Is.EqualTo(0));
        }

        [Test]
        public void ShopRental_BooksItsOwnRevenueSlot_SeparateFromTheOlderRentalSubsystem()
        {
            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "shop_rental", gross: 8000, fee: 262, cut: 240, net: 7498, tax: 480) },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueBikeShopRental), Is.EqualTo(-7520));
            Assert.That(Signed(d, QboAccountKeys.RevenueRental), Is.EqualTo(0),
                "the bike shop rental subsystem is not the older rental_purchase one");
            Assert.That(Signed(d, QboAccountKeys.LiabilitySalesTax), Is.EqualTo(-480));
        }

        [Test]
        public void ShopRentalDeposit_IsDamageIncome_NotRentalRevenue()
        {
            // BikeShopRentalController writes this row only for the amount actually captured out of
            // a damage hold, so it is earned income and shares the forfeited-deposit slot.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "shop_rental_deposit", gross: 6000, fee: 204, cut: 0, net: 5796) },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueDepositForfeited), Is.EqualTo(-6000));
            Assert.That(Signed(d, QboAccountKeys.RevenueBikeShopRental), Is.EqualTo(0));
        }

        [Test]
        public void WorkOrderDepositAndItsBillOut_TogetherCreditTheWholeJob()
        {
            // A $500 job with a $150 deposit taken up front. The deposit books its own row, and the
            // bill-out books only the remainder (OnShopSalePaid: total - deposit_applied -
            // credit_applied), so the two sum to the job and neither double counts the other. Both
            // are bike shop revenue; the deposit is simply recognized earlier.
            var d = JournalEntryBuilder.Build(
                new[]
                {
                    Sale(sourceKind: "shop_wo_deposit", gross: 15000, fee: 465, cut: 450, net: 14085),
                    Sale(sourceKind: "shop_sale", gross: 35000, fee: 1045, cut: 1050, net: 32905),
                },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueBikeShop), Is.EqualTo(-50000),
                "deposit plus remainder is the whole job, booked once");
            Assert.That(Signed(d, QboAccountKeys.RevenueOther), Is.EqualTo(0),
                "the deposit no longer falls through to the catch-all slot");
        }

        [Test]
        public void CashSalePartFundedByAGiftCard_DebitsOnlyWhatReachedTheDrawer()
        {
            // The bike shop register books gross = cash + gift on a cash sale and folds the gift
            // float into net (net = gift - cut). Debiting the whole gross to the drawer would
            // overstate the till by the gift amount and leave the day unbalanced outright.
            // $80 sale: $50 cash, $30 off a gift card, $2.40 cut.
            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "shop_sale", gross: 8000, fee: 0, cut: 240, net: 3000 - 240,
                             giftCard: 3000, paymentMethod: "cash") },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.AssetUndepositedCash), Is.EqualTo(5000),
                "only the 50 in notes reached the till");
            Assert.That(Signed(d, QboAccountKeys.LiabilityGiftCard), Is.EqualTo(3000));
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(-240),
                "the track holds the cash, so it owes us our cut and nothing else");
        }

        // ── Department split by event type ───────────────────────────────────────────────
        // A lesson, a camp and a lift ticket are all the same source_kind ('event_ticket'), because
        // all three are just an `event` with tickets on it. The only thing that can tell them apart
        // is the event TYPE, which the tenant maps to a revenue slot (tenant_event_type.revenue_key,
        // Script0274), carried here as RevenueKeyOverride.

        [Test]
        public void EventTicketWithATrainingOverride_CreditsTrainingRevenue_NotTheGate()
        {
            // A $150 clinic seat sold at Highland, whose "Clinic" event type points at the Training
            // Center slot. It must not land next to the lift tickets.
            var d = JournalEntryBuilder.Build(
                new[]
                {
                    Sale(sourceKind: "event_ticket", gross: 15000, fee: 465, cut: 450, net: 14085,
                         revenueKeyOverride: QboAccountKeys.RevenueTraining),
                },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueTraining), Is.EqualTo(-15000));
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(0),
                "gate revenue must be untouched, or the department split is invisible on the P&L");
            Assert.That(Signed(d, QboAccountKeys.AssetRidepassReceivable), Is.EqualTo(14085),
                "only the revenue SLOT moves; every tender and fee term is unchanged");
        }

        [Test]
        public void AnUnknownOverrideKey_FallsBackToTheSourceKindSlot()
        {
            // A key from a newer schema can reach an older deployment mid-rollout, and an account
            // slot no tenant has mapped blocks the whole day's post. Falling back books the day the
            // way it was booked yesterday, which is strictly better than not booking it at all.
            var d = JournalEntryBuilder.Build(
                new[]
                {
                    Sale(sourceKind: "event_ticket", gross: 4400, fee: 157, cut: 400, net: 3843,
                         revenueKeyOverride: "revenue_bicycle_polo"),
                },
                Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(-4400));
            Assert.That(d.Lines.Any(l => l.AccountKey == "revenue_bicycle_polo"), Is.False,
                "an unmapped slot must never reach QuickBooks");
        }

        [Test]
        public void AMixedDayOfGateAndTraining_SplitsIntoTwoLinesThatSumToTheOldOne()
        {
            // The migration's whole promise: the same money, reported as two departments. A $44 lift
            // ticket and a $150 clinic seat on one day.
            var gate = Sale(sourceKind: "event_ticket", gross: 4400, fee: 157, cut: 400, net: 3843);
            var lesson = Sale(sourceKind: "event_ticket", gross: 15000, fee: 465, cut: 450, net: 14085,
                              revenueKeyOverride: QboAccountKeys.RevenueTraining);

            var d = JournalEntryBuilder.Build(new[] { gate, lesson }, Day);

            AssertBalanced(d);
            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket), Is.EqualTo(-4400));
            Assert.That(Signed(d, QboAccountKeys.RevenueTraining), Is.EqualTo(-15000));

            // What the same day booked before the split existed: one line for the lot.
            var before = JournalEntryBuilder.Build(
                new[] { gate, Sale(sourceKind: "event_ticket", gross: 15000, fee: 465, cut: 450, net: 14085) },
                Day);

            Assert.That(Signed(d, QboAccountKeys.RevenueEventTicket) + Signed(d, QboAccountKeys.RevenueTraining),
                Is.EqualTo(Signed(before, QboAccountKeys.RevenueEventTicket)),
                "splitting a department out must move revenue between lines, never create or destroy it");
            Assert.That(d.TotalDebitCents, Is.EqualTo(before.TotalDebitCents),
                "and the day's totals are identical either way");
        }

        // ── Profit centers (QBO classes) ────────────────────────────────

        [Test]
        public void WithNoClassMap_EveryLineIsUnclassed()
        {
            // The safety property for every track that never opts in: passing no class map has to
            // produce the draft this built before classes existed, line for line.
            var entries = new[]
            {
                Sale(sourceKind: "event_ticket", gross: 4400, fee: 157, cut: 400, net: 3843, tax: 300),
                Sale(sourceKind: "concession", gross: 1200, fee: 65, cut: 100, net: 1035, tip: 200),
            };

            var d = JournalEntryBuilder.Build(entries, Day);

            AssertBalanced(d);
            Assert.That(d.Lines.All(l => l.ClassId is null), Is.True);
        }

        [Test]
        public void WithAClassMap_OnlyRevenueLinesCarryTheClass()
        {
            // Tax, tips, fees and tenders belong to no single business unit. Stamping them with one
            // center's class would silently attribute another center's costs to it.
            var map = new Dictionary<string, string>
            {
                [QboAccountKeys.RevenueConcession] = "5",
            };

            var d = JournalEntryBuilder.Build(
                new[] { Sale(sourceKind: "concession", gross: 1200, fee: 65, cut: 100, net: 1035, tax: 90, tip: 200) },
                Day, map);

            AssertBalanced(d);
            Assert.That(d.Lines.Single(l => l.AccountKey == QboAccountKeys.RevenueConcession).ClassId, Is.EqualTo("5"));
            Assert.That(d.Lines.Where(l => l.AccountKey != QboAccountKeys.RevenueConcession).All(l => l.ClassId is null),
                Is.True, "only income lines may carry a class");
        }

        [Test]
        public void TwoCentersOnOneDay_KeepTheirOwnClasses()
        {
            var map = new Dictionary<string, string>
            {
                [QboAccountKeys.RevenueBikeShop] = "shop",
                [QboAccountKeys.RevenueConcession] = "food",
            };

            var d = JournalEntryBuilder.Build(
                new[]
                {
                    Sale(sourceKind: "shop_sale", gross: 8500, fee: 277, cut: 425, net: 7798),
                    Sale(sourceKind: "concession", gross: 1200, fee: 65, cut: 100, net: 1035),
                },
                Day, map);

            AssertBalanced(d);
            Assert.That(d.Lines.Single(l => l.AccountKey == QboAccountKeys.RevenueBikeShop).ClassId, Is.EqualTo("shop"));
            Assert.That(d.Lines.Single(l => l.AccountKey == QboAccountKeys.RevenueConcession).ClassId, Is.EqualTo("food"));
        }

        [Test]
        public void AClassMap_ChangesNoAmountLineCountOrBalance()
        {
            // Classes are presentation inside QuickBooks. If mapping one could move a cent, it would
            // be a money bug wearing a reporting feature's clothes, so pin the whole draft.
            var entries = new[]
            {
                Sale(sourceKind: "event_ticket", gross: 4400, fee: 157, cut: 400, net: 3843, tax: 300),
                Sale(sourceKind: "concession", gross: 1200, fee: 65, cut: 100, net: 1035, tip: 200),
                Sale(sourceKind: "shop_sale", gross: 8500, fee: 277, cut: 425, net: 7798, giftCard: 2000),
            };
            var map = new Dictionary<string, string>
            {
                [QboAccountKeys.RevenueEventTicket] = "gate",
                [QboAccountKeys.RevenueConcession] = "food",
                [QboAccountKeys.RevenueBikeShop] = "shop",
            };

            var plain = JournalEntryBuilder.Build(entries, Day);
            var classed = JournalEntryBuilder.Build(entries, Day, map);

            AssertBalanced(classed);
            Assert.That(classed.Lines.Count, Is.EqualTo(plain.Lines.Count));
            Assert.That(classed.TotalDebitCents, Is.EqualTo(plain.TotalDebitCents));
            Assert.That(
                classed.Lines.Select(l => (l.AccountKey, l.IsDebit, l.AmountCents)),
                Is.EqualTo(plain.Lines.Select(l => (l.AccountKey, l.IsDebit, l.AmountCents))),
                "the class map may only add a tag; it may never change a line");
        }

        [Test]
        public void APartiallyMappedTenant_StillPostsTheUnmappedCentersUnclassed()
        {
            // A track that has named a class for their shop but not their kitchen must still post.
            // Blocking the day over a reporting tag would be a self-inflicted outage.
            var map = new Dictionary<string, string> { [QboAccountKeys.RevenueBikeShop] = "shop" };

            var d = JournalEntryBuilder.Build(
                new[]
                {
                    Sale(sourceKind: "shop_sale", gross: 8500, fee: 277, cut: 425, net: 7798),
                    Sale(sourceKind: "concession", gross: 1200, fee: 65, cut: 100, net: 1035),
                },
                Day, map);

            AssertBalanced(d);
            Assert.That(d.Lines.Single(l => l.AccountKey == QboAccountKeys.RevenueBikeShop).ClassId, Is.EqualTo("shop"));
            Assert.That(d.Lines.Single(l => l.AccountKey == QboAccountKeys.RevenueConcession).ClassId, Is.Null);
        }

        [Test]
        public void AnEventTypeOverride_TakesTheClassOfTheSlotItLandsIn()
        {
            // The override moves a lift-ticket sale into the training slot, so it must pick up the
            // TRAINING center's class, not the gate's. This is the case a naive
            // "class per source kind" implementation would get wrong.
            var map = new Dictionary<string, string>
            {
                [QboAccountKeys.RevenueEventTicket] = "gate",
                [QboAccountKeys.RevenueTraining] = "training",
            };

            var d = JournalEntryBuilder.Build(
                new[]
                {
                    Sale(sourceKind: "event_ticket", gross: 4400, fee: 157, cut: 400, net: 3843),
                    Sale(sourceKind: "event_ticket", gross: 15000, fee: 465, cut: 450, net: 14085,
                         revenueKeyOverride: QboAccountKeys.RevenueTraining),
                },
                Day, map);

            AssertBalanced(d);
            Assert.That(d.Lines.Single(l => l.AccountKey == QboAccountKeys.RevenueEventTicket).ClassId, Is.EqualTo("gate"));
            Assert.That(d.Lines.Single(l => l.AccountKey == QboAccountKeys.RevenueTraining).ClassId, Is.EqualTo("training"));
        }

        [Test]
        public void ARefund_CarriesTheSameClassAsTheSaleItReverses()
        {
            // A refunded day nets the revenue line to a DEBIT. It still belongs to the center that
            // took the money, otherwise a P&L by class shows income in one column and its reversal
            // in another.
            var map = new Dictionary<string, string> { [QboAccountKeys.RevenueConcession] = "food" };

            var d = JournalEntryBuilder.Build(
                new[]
                {
                    Sale(sourceKind: "concession", gross: 1200, fee: 65, cut: 100, net: 1035),
                    Sale(sourceKind: "concession", gross: -2000, fee: -100, cut: -150, net: -1750,
                         entryKind: "refund"),
                },
                Day, map);

            AssertBalanced(d);
            var revenue = d.Lines.Single(l => l.AccountKey == QboAccountKeys.RevenueConcession);
            Assert.That(revenue.IsDebit, Is.True, "the day net-refunded, so revenue lands on the debit side");
            Assert.That(revenue.ClassId, Is.EqualTo("food"));
        }
    }
}

