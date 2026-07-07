using NUnit.Framework;
using Services.Payments;

namespace UnitTests
{
    // Covers the refund ledger math extracted from PurchaseController.RefundOne. The invariant that
    // matters: for every funding mode, a FULL refund must reverse exactly what the sale booked, so the
    // pair (sale entry + refund entry) nets the tenant's balance and our cut back to zero — except the
    // plain-Stripe case, where a tenant-initiated refund intentionally leaves our prior cut in place.
    [TestFixture]
    public class RefundLedgerMathTests
    {
        private const int Gross = 10_000;        // $100.00 charged (incl. service charge)
        private const int ServiceCharge = 1_000; // $10.00 service charge = our cut

        // Sale-side booking these fixtures assume (matches FeeCalculator + the free-cart ledger paths,
        // no monthly cap): the tuple is (gross, cut, net).
        private static (int gross, int cut, int net) PlatformStripeSale() => (Gross, ServiceCharge, Gross - ServiceCharge);
        private static (int gross, int cut, int net) CashSale() => (Gross, ServiceCharge, -ServiceCharge);
        private static (int gross, int cut, int net) DirectSale() => (Gross, ServiceCharge, 0);
        private static (int gross, int cut, int net) PlatformGiftCardSale() => (Gross, ServiceCharge, Gross - ServiceCharge);
        private static (int gross, int cut, int net) DirectGiftCardSale() => (Gross, 0, 0);

        [Test]
        public void PlatformStripe_FullRefund_DebitsFullAmount_KeepsCut()
        {
            var r = RefundLedgerMath.Compute("stripe", Gross, ServiceCharge,
                refundCents: Gross, stripeRefundCents: Gross, giftCardRefundCents: 0, isDirect: false);

            Assert.That(r.GrossCents, Is.EqualTo(-Gross));
            Assert.That(r.RidepassCutCents, Is.EqualTo(0), "plain-Stripe refund does not claw back our cut");
            Assert.That(r.NetToTenantCents, Is.EqualTo(-Gross), "tenant payout is debited the full refund");
        }

        [Test]
        public void StripeDirect_FullRefund_ReversesAppFee_NetStaysZero()
        {
            // The bug (#4): stripe_direct fell into the plain-Stripe branch and wrote net = -refund,
            // leaving a permanent negative for money the platform never held.
            var r = RefundLedgerMath.Compute("stripe_direct", Gross, ServiceCharge,
                refundCents: Gross, stripeRefundCents: Gross, giftCardRefundCents: 0, isDirect: true);

            Assert.That(r.NetToTenantCents, Is.EqualTo(0), "direct tenant holds funds on its own account");
            Assert.That(r.RidepassCutCents, Is.EqualTo(-ServiceCharge), "the returned application fee is reversed");
            AssertSaleAndRefundNetToZero(DirectSale(), r);
        }

        [Test]
        public void PlatformGiftCard_FullRefund_ReversesCredit_NoDoubleDebit()
        {
            // The bug (#5): a fully gift-card-funded refund hit the plain-Stripe branch (net = -refund),
            // double-debiting the tenant, who had only been credited net = gross - cut.
            var r = RefundLedgerMath.Compute("stripe", Gross, ServiceCharge,
                refundCents: Gross, stripeRefundCents: 0, giftCardRefundCents: Gross, isDirect: false);

            Assert.That(r.NetToTenantCents, Is.EqualTo(-(Gross - ServiceCharge)));
            Assert.That(r.NetToTenantCents, Is.Not.EqualTo(-Gross), "must not double-debit like the plain-Stripe branch");
            Assert.That(r.RidepassCutCents, Is.EqualTo(-ServiceCharge));
            AssertSaleAndRefundNetToZero(PlatformGiftCardSale(), r);
        }

        [Test]
        public void DirectGiftCard_FullRefund_NetsToZero()
        {
            var r = RefundLedgerMath.Compute("stripe", Gross, ServiceCharge,
                refundCents: Gross, stripeRefundCents: 0, giftCardRefundCents: Gross, isDirect: true);

            Assert.That(r.RidepassCutCents, Is.EqualTo(0));
            Assert.That(r.NetToTenantCents, Is.EqualTo(0));
            AssertSaleAndRefundNetToZero(DirectGiftCardSale(), r);
        }

        [Test]
        public void Cash_FullRefund_ReversesOwedCut()
        {
            var r = RefundLedgerMath.Compute("cash", Gross, ServiceCharge,
                refundCents: Gross, stripeRefundCents: 0, giftCardRefundCents: 0, isDirect: false);

            Assert.That(r.NetToTenantCents, Is.EqualTo(ServiceCharge), "reverses the cash sale's net = -serviceCharge");
            Assert.That(r.RidepassCutCents, Is.EqualTo(-ServiceCharge));
            AssertSaleAndRefundNetToZero(CashSale(), r);
        }

        [Test]
        public void Voucher_FullRefund_NetsToZero()
        {
            // A voucher ($0 sale) booked gross/cut/net all zero, so its refund is all zeros too.
            var r = RefundLedgerMath.Compute("voucher", 0, 0,
                refundCents: 0, stripeRefundCents: 0, giftCardRefundCents: 0, isDirect: false);

            Assert.That(r.GrossCents, Is.EqualTo(0));
            Assert.That(r.RidepassCutCents, Is.EqualTo(0));
            Assert.That(r.NetToTenantCents, Is.EqualTo(0));
        }

        [Test]
        public void StripeDirect_PartialRefund_ProratesCut()
        {
            // Refund half the order: our reversed cut should be half the service charge.
            var half = Gross / 2;
            var r = RefundLedgerMath.Compute("stripe_direct", Gross, ServiceCharge,
                refundCents: half, stripeRefundCents: half, giftCardRefundCents: 0, isDirect: true);

            Assert.That(r.GrossCents, Is.EqualTo(-half));
            Assert.That(r.RidepassCutCents, Is.EqualTo(-ServiceCharge / 2));
            Assert.That(r.NetToTenantCents, Is.EqualTo(0));
        }

        [Test]
        public void PlatformGiftCard_PartialRefund_ProratesCreditReversal()
        {
            var half = Gross / 2;
            var r = RefundLedgerMath.Compute("stripe", Gross, ServiceCharge,
                refundCents: half, stripeRefundCents: 0, giftCardRefundCents: half, isDirect: false);

            var proratedCut = ServiceCharge / 2;
            Assert.That(r.RidepassCutCents, Is.EqualTo(-proratedCut));
            Assert.That(r.NetToTenantCents, Is.EqualTo(-(half - proratedCut)));
        }

        [Test]
        public void ZeroAmount_NeverDividesByZero()
        {
            var r = RefundLedgerMath.Compute("stripe_direct", amountCents: 0, serviceChargeCents: 0,
                refundCents: 0, stripeRefundCents: 0, giftCardRefundCents: 0, isDirect: true);
            Assert.That(r.RidepassCutCents, Is.EqualTo(0));
        }

        // Asserts that adding the signed refund row to the positive sale row zeroes gross, cut, and net.
        private static void AssertSaleAndRefundNetToZero((int gross, int cut, int net) sale, RefundLedgerAmounts refund)
        {
            Assert.That(sale.gross + refund.GrossCents, Is.EqualTo(0), "gross should net to zero");
            Assert.That(sale.cut + refund.RidepassCutCents, Is.EqualTo(0), "cut should net to zero");
            Assert.That(sale.net + refund.NetToTenantCents, Is.EqualTo(0), "net-to-tenant should net to zero");
        }
    }
}
