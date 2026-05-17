namespace Services.Helpers
{
    /// <summary>
    /// Cancellation/refund math. Core rule: refunds never give back the rider's
    /// service-charge portion — that's earned the moment the rider clicks Buy.
    ///
    /// Inputs come straight off the purchase row:
    ///   amountCents           — gross charged to the rider (subtotal + rider portion of fee)
    ///   serviceChargeCents    — the *full* tenant service charge stored on the row
    ///   riderPaidServiceChargeBps — what fraction of that fee the rider actually paid
    ///                                (0–10000; 10000 = rider paid 100%)
    ///
    /// Returns the cents to refund (always &gt;= 0).
    /// </summary>
    public static class RefundCalculator
    {
        public static int RefundableCents(int amountCents, int serviceChargeCents, int riderPaidServiceChargeBps)
        {
            var riderPortion = (int)((long)serviceChargeCents * riderPaidServiceChargeBps / 10_000L);
            var refund = amountCents - riderPortion;
            return refund < 0 ? 0 : refund;
        }
    }
}
