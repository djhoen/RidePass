using NUnit.Framework;
using Services.Concessions;
using Services.Repositories.Data.ConcessionData;

namespace UnitTests
{
    // The decisions ConcessionController makes for F&B open tabs, extracted so they can be pinned
    // down without a database: how a tab's totals are rebuilt from every round's lines, which tender
    // path a close takes, and which statuses may be voided or refunded.
    [TestFixture]
    public class ConcessionTabRulesTests
    {
        private static ConcessionSaleLine Line(int net, int discount = 0, int tax = 0, int round = 1) => new()
        {
            NameSnapshot = "item",
            Quantity = 1,
            UnitPriceCents = net + discount,
            LineTotalCents = net,
            DiscountCents = discount,
            TaxCents = tax,
            RoundNumber = round,
        };

        // ── Totals across rounds ──────────────────────────────────────────────────

        [Test]
        public void Recompute_SumsEveryRound_ExclusiveTaxOnTop()
        {
            var lines = new[]
            {
                Line(net: 1_000, tax: 80, round: 1),
                Line(net: 500, tax: 40, round: 1),
                Line(net: 1_200, tax: 96, round: 2),
            };
            var t = ConcessionTabRules.Recompute(lines, pricesIncludeTax: false, tipCents: 300);
            Assert.Multiple(() =>
            {
                Assert.That(t.SubtotalCents, Is.EqualTo(2_700));
                Assert.That(t.DiscountCents, Is.EqualTo(0));
                Assert.That(t.TaxCents, Is.EqualTo(216));
                Assert.That(t.TotalCents, Is.EqualTo(2_700 + 216 + 300));
            });
        }

        [Test]
        public void Recompute_SubtotalStaysGross_DiscountSubtracted()
        {
            // Lines carry NET totals; a $2.00 line discount on a $10.00 line is net 800 / discount 200.
            var lines = new[] { Line(net: 800, discount: 200, tax: 64, round: 1), Line(net: 1_000, tax: 80, round: 2) };
            var t = ConcessionTabRules.Recompute(lines, pricesIncludeTax: false, tipCents: 0);
            Assert.Multiple(() =>
            {
                Assert.That(t.SubtotalCents, Is.EqualTo(2_000), "gross, pre-discount, like CreateSale");
                Assert.That(t.DiscountCents, Is.EqualTo(200));
                Assert.That(t.TotalCents, Is.EqualTo(2_000 - 200 + 144));
            });
        }

        [Test]
        public void Recompute_InclusiveTaxIsNotAddedAgain()
        {
            var lines = new[] { Line(net: 1_080, tax: 80, round: 1), Line(net: 1_080, tax: 80, round: 2) };
            var t = ConcessionTabRules.Recompute(lines, pricesIncludeTax: true, tipCents: 0);
            Assert.That(t.TotalCents, Is.EqualTo(2_160));
            Assert.That(t.TaxCents, Is.EqualTo(160), "reported, not added");
        }

        [Test]
        public void Recompute_IgnoresNegativeTip_AndNeverGoesBelowZero()
        {
            var t = ConcessionTabRules.Recompute(new[] { Line(net: 0, discount: 500) }, false, tipCents: -100);
            Assert.That(t.TotalCents, Is.EqualTo(0), "a fully comped tab is $0, not negative");
        }

        [Test]
        public void NextRoundNumber_IsOnePastTheHighestFired()
        {
            Assert.That(ConcessionTabRules.NextRoundNumber(Array.Empty<ConcessionSaleLine>()), Is.EqualTo(1));
            Assert.That(ConcessionTabRules.NextRoundNumber(new[] { Line(100, round: 1), Line(100, round: 3) }), Is.EqualTo(4));
        }

        // ── Close-tab tender branching ────────────────────────────────────────────

        [Test]
        public void Close_CashPaysNow()
        {
            Assert.That(ConcessionTabRules.ResolveClosePath("cash", 2_500, null, null), Is.EqualTo(TabClosePath.PaidNow));
        }

        [Test]
        public void Close_NothingDuePaysNow_WhateverTenderWasAsked()
        {
            // Credit or a full comp covered it: no card is touched even if the POS said 'card' / 'hold'.
            Assert.That(ConcessionTabRules.ResolveClosePath("card", 0, null, null), Is.EqualTo(TabClosePath.PaidNow));
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 0, "pi_hold", 5_000), Is.EqualTo(TabClosePath.PaidNow));
        }

        [Test]
        public void Close_CardNeedsTheProcessorMinimum()
        {
            Assert.That(ConcessionTabRules.ResolveClosePath("card", 49, null, null), Is.EqualTo(TabClosePath.CardBelowMinimum));
            Assert.That(ConcessionTabRules.ResolveClosePath("card", 50, null, null), Is.EqualTo(TabClosePath.CardPresent));
        }

        [Test]
        public void Close_HoldCapturesWhenItCoversTheTotal()
        {
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 4_999, "pi_hold", 5_000), Is.EqualTo(TabClosePath.CaptureHold));
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 5_000, "pi_hold", 5_000), Is.EqualTo(TabClosePath.CaptureHold));
        }

        [Test]
        public void Close_HoldTooSmallWhenTheTabOutgrewIt()
        {
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 5_001, "pi_hold", 5_000), Is.EqualTo(TabClosePath.HoldTooSmall));
        }

        [Test]
        public void Close_HoldBelowTheProcessorMinimumIsNotCaptured()
        {
            // Stripe rejects a capture under 50 cents like it rejects a charge under 50 cents, so a
            // tiny remainder after credit on a held card is a cash remainder (same message the POS
            // already gets for 'card'), never a capture attempt.
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 49, "pi_hold", 5_000), Is.EqualTo(TabClosePath.CardBelowMinimum));
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 1, "pi_hold", 5_000), Is.EqualTo(TabClosePath.CardBelowMinimum));
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 50, "pi_hold", 5_000), Is.EqualTo(TabClosePath.CaptureHold));
            // Nothing due still pays now, and a missing hold is still reported as such first.
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 0, "pi_hold", 5_000), Is.EqualTo(TabClosePath.PaidNow));
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 10, null, null), Is.EqualTo(TabClosePath.NoHold));
        }

        // ── Serialized mutations: in-flight card, conditional flips ───────────────

        [Test]
        public void CardInFlight_OnlyAnOpenTabPointingAtAnIntent()
        {
            Assert.That(ConcessionTabRules.HasCardInFlight("open", "pi_card"), Is.True);
            Assert.That(ConcessionTabRules.HasCardInFlight("open", null), Is.False);
            Assert.That(ConcessionTabRules.HasCardInFlight("open", ""), Is.False);
            Assert.That(ConcessionTabRules.HasCardInFlight("paid", "pi_card"), Is.False, "a paid tab's intent is its settled payment, not a close in flight");
        }

        [Test]
        public void InFlightCancel_OnlyAConfirmedCancelReleasesTheTab()
        {
            Assert.That(ConcessionTabRules.ResolveInFlightCancel("canceled"), Is.EqualTo(InFlightCardOutcome.Released));
            Assert.That(ConcessionTabRules.ResolveInFlightCancel("succeeded"), Is.EqualTo(InFlightCardOutcome.AlreadyPaid), "the customer paid in the window: finalize, never proceed");
            Assert.That(ConcessionTabRules.ResolveInFlightCancel(null), Is.EqualTo(InFlightCardOutcome.Blocked), "Stripe unreachable: nothing is known to be safe");
            Assert.That(ConcessionTabRules.ResolveInFlightCancel("processing"), Is.EqualTo(InFlightCardOutcome.Blocked));
            Assert.That(ConcessionTabRules.ResolveInFlightCancel("requires_capture"), Is.EqualTo(InFlightCardOutcome.Blocked));
        }

        [Test]
        public void PaidIntent_BooksOnlyASaleStillWaitingOnThatIntent()
        {
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("open", "pi_1", "pi_1"), Is.EqualTo(PaidIntentOutcome.Book));
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("pending", "pi_1", "pi_1"), Is.EqualTo(PaidIntentOutcome.Book));
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("paid", "pi_1", "pi_1"), Is.EqualTo(PaidIntentOutcome.AlreadyBooked), "duplicate webhook");
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("refunded", "pi_1", "pi_1"), Is.EqualTo(PaidIntentOutcome.AlreadyBooked));
        }

        [Test]
        public void PaidIntent_MoneyLandingOnASettledOrVoidedTabIsRefunded()
        {
            // Cash close won the race, then the card (or captured hold) succeeded: refund the stray charge.
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("paid", null, "pi_hold"), Is.EqualTo(PaidIntentOutcome.UnbookedRefund));
            // Paid by a different card: a double charge.
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("paid", "pi_2", "pi_1"), Is.EqualTo(PaidIntentOutcome.UnbookedRefund));
            // Voided with the intent still on it, or its hold captured afterwards.
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("voided", "pi_1", "pi_1"), Is.EqualTo(PaidIntentOutcome.UnbookedRefund));
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("voided", null, "pi_hold"), Is.EqualTo(PaidIntentOutcome.UnbookedRefund));
        }

        [Test]
        public void PaidIntent_MoneyLandingOnALiveSaleIsKeptForAHuman()
        {
            // A hold captured out of band while the tab is still open: the close in progress will book it.
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("open", null, "pi_hold"), Is.EqualTo(PaidIntentOutcome.UnbookedKeep));
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("open", "pi_2", "pi_1"), Is.EqualTo(PaidIntentOutcome.UnbookedKeep));
            // A dead counter sale whose intent later succeeded: never fulfilled; flagged, not auto-refunded.
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("failed", "pi_1", "pi_1"), Is.EqualTo(PaidIntentOutcome.UnbookedKeep));
            Assert.That(ConcessionTabRules.ClassifyPaidIntent("abandoned", "pi_1", "pi_1"), Is.EqualTo(PaidIntentOutcome.UnbookedKeep));
        }

        [Test]
        public void Close_HoldWithoutACardOnFileIsRejected()
        {
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 1_000, null, null), Is.EqualTo(TabClosePath.NoHold));
            Assert.That(ConcessionTabRules.ResolveClosePath("hold", 1_000, "", 5_000), Is.EqualTo(TabClosePath.NoHold));
        }

        [Test]
        public void Close_UnknownTenderIsInvalid()
        {
            Assert.That(ConcessionTabRules.ResolveClosePath("venmo", 1_000, null, null), Is.EqualTo(TabClosePath.Invalid));
            Assert.That(ConcessionTabRules.ResolveClosePath(null, 1_000, null, null), Is.EqualTo(TabClosePath.Invalid));
        }

        [Test]
        public void Close_TenderIsCaseAndWhitespaceInsensitive()
        {
            Assert.That(ConcessionTabRules.ResolveClosePath(" Card ", 1_000, null, null), Is.EqualTo(TabClosePath.CardPresent));
        }

        // ── Void / refund status gates ────────────────────────────────────────────

        [Test]
        public void Void_OnlyAnOpenTabCanBeVoided()
        {
            Assert.That(ConcessionTabRules.VoidBlocker("open"), Is.Null);
            Assert.That(ConcessionTabRules.VoidBlocker("paid"), Is.Not.Null);
            Assert.That(ConcessionTabRules.VoidBlocker("voided"), Is.Not.Null);
            Assert.That(ConcessionTabRules.VoidBlocker("pending"), Is.Not.Null);
            Assert.That(ConcessionTabRules.VoidBlocker("refunded"), Is.Not.Null);
        }

        [Test]
        public void Refund_RejectsOpenAndVoidedTabs_KeepsPaid()
        {
            Assert.That(ConcessionTabRules.RefundBlocker("paid"), Is.Null);
            Assert.That(ConcessionTabRules.RefundBlocker("open"), Is.Not.Null);
            Assert.That(ConcessionTabRules.RefundBlocker("voided"), Is.Not.Null);
            // The pre-tabs wording for every other status is preserved.
            Assert.That(ConcessionTabRules.RefundBlocker("refunded"), Is.EqualTo("A refunded sale can't be refunded."));
        }

        // ── Labels ───────────────────────────────────────────────────────────────

        [Test]
        public void MergeDiscountKind_SameStays_DifferentBecomesMixed()
        {
            Assert.That(ConcessionTabRules.MergeDiscountKind(null, "comp"), Is.EqualTo("comp"));
            Assert.That(ConcessionTabRules.MergeDiscountKind("comp", null), Is.EqualTo("comp"));
            Assert.That(ConcessionTabRules.MergeDiscountKind("comp", "comp"), Is.EqualTo("comp"));
            Assert.That(ConcessionTabRules.MergeDiscountKind("comp", "percent"), Is.EqualTo("mixed"));
        }

        [Test]
        public void AutoTabName_FromTheHeldCard()
        {
            Assert.That(ConcessionTabRules.AutoTabName("visa", "4242", "John Smith"), Is.EqualTo("Visa ••4242 · JOHN SMITH"));
            Assert.That(ConcessionTabRules.AutoTabName("mastercard", "1111", null), Is.EqualTo("Mastercard ••1111"));
            Assert.That(ConcessionTabRules.AutoTabName(null, null, null), Is.Null);
            Assert.That(ConcessionTabRules.HoldCardLabel("amex", "0005"), Is.EqualTo("Amex ••0005"));
        }
    }
}
