using System;

namespace Services.Payments
{
    /// <summary>Signed tenant_ledger_entry amounts for a refund row.</summary>
    public readonly record struct RefundLedgerAmounts(int GrossCents, int RidepassCutCents, int NetToTenantCents);

    /// <summary>
    /// Computes the ledger amounts for a refund from the positive sale-side figures. The rules depend
    /// on how the sale was funded, because the tenant's platform payout may or may not have been
    /// credited the money in the first place:
    ///
    ///   • cash / voucher — the tenant always held the cash; the sale booked net = -serviceCharge (the
    ///     tenant owes us our cut). Reverse the proportional cut so a full refund nets to zero.
    ///   • gift-card-funded (no Stripe money moved) — the sale was booked via the gift-card-covered
    ///     path: a platform tenant was credited net = gross - cut with our cut booked; a direct tenant
    ///     already held the float, so net = 0 / cut = 0. Reverse that (the gift-card balance itself is
    ///     restored separately). Prevents the double-debit of the plain-Stripe branch.
    ///   • stripe_direct — the charge ran on the tenant's own account (sale net = 0, cut = the app fee);
    ///     the refund returned that app fee, so reverse the proportional cut and keep net = 0.
    ///   • stripe / stripe_connect — the platform moved the money, so the refund debits the tenant's
    ///     payout by the refunded amount. Our prior cut is not clawed back on a tenant-initiated refund.
    /// </summary>
    public static class RefundLedgerMath
    {
        public static RefundLedgerAmounts Compute(
            string paymentMethod,
            int amountCents,
            int serviceChargeCents,
            int refundCents,
            int stripeRefundCents,
            int giftCardRefundCents,
            bool isDirect)
        {
            int CutOnRefund() => amountCents > 0
                ? (int)Math.Round((double)serviceChargeCents * refundCents / amountCents, MidpointRounding.AwayFromZero)
                : 0;

            if (paymentMethod is "cash" or "voucher")
            {
                var cut = CutOnRefund();
                return new RefundLedgerAmounts(-refundCents, -cut, cut);
            }

            if (giftCardRefundCents > 0 && stripeRefundCents == 0)
            {
                var cut = isDirect ? 0 : CutOnRefund();
                var net = isDirect ? 0 : -(refundCents - cut);
                return new RefundLedgerAmounts(-refundCents, -cut, net);
            }

            if (paymentMethod == "stripe_direct")
            {
                var cut = CutOnRefund();
                return new RefundLedgerAmounts(-refundCents, -cut, 0);
            }

            return new RefundLedgerAmounts(-refundCents, 0, -refundCents);
        }
    }
}
