using System;

namespace Services.Payments
{
    /// <summary>
    /// What a rental costs, split into the parts that matter for the ledger and the card.
    /// </summary>
    /// <param name="ServiceChargeCents">
    /// The full RidePass service charge on the rental. What we are owed, whoever ends up funding it.
    /// </param>
    /// <param name="RiderServiceChargeCents">
    /// The renter-funded share of that charge, which is the only part added to what the card is
    /// charged. A track absorbing the fee (riderPaidBps = 0) still owes ServiceChargeCents, out of
    /// its own proceeds.
    /// </param>
    /// <param name="RentalAmountCents">Subtotal plus the renter's share of the fee. Deposit-free.</param>
    /// <param name="DepositCents">The refundable security deposit.</param>
    /// <param name="GrossCents">
    /// Rental plus deposit: what a single charge would come to if the deposit were taken on the same
    /// intent. The shop rental flow does NOT charge this, because it holds the deposit as a separate
    /// manual-capture authorization that is never captured unless damage is kept.
    /// </param>
    public readonly record struct RentalChargeAmounts(
        int ServiceChargeCents,
        int RiderServiceChargeCents,
        int RentalAmountCents,
        int DepositCents,
        int GrossCents);

    /// <summary>
    /// Rental pricing, in one place, so the one invariant that matters cannot drift:
    ///
    ///   <b>The service charge is computed on the rental subtotal ONLY, never on the deposit.</b>
    ///
    /// A security deposit is the renter's own money held against damage, not consideration for the
    /// rental. Taking a percentage of it would be charging someone a fee for the privilege of
    /// lending us their deposit. RentalChargeTests pins this; the deposit is carried through these
    /// calculations untouched and never enters a fee base.
    ///
    /// Integer math matches the wire exactly: the tenant charge is floored, then the renter's share
    /// of it is floored again. Both are truncating integer divisions on long intermediates, so a
    /// quoted total always equals what the card is actually charged. Do not "improve" this to
    /// rounding without changing the client-side estimate in lockstep.
    /// </summary>
    public static class RentalCharge
    {
        /// <summary>
        /// One rental product taken in some quantity, where the deposit is quoted per unit.
        /// </summary>
        public static RentalChargeAmounts Compute(
            int subtotalAfterDiscountCents,
            int serviceChargeBps,
            int riderPaidServiceChargeBps,
            int depositPerUnitCents,
            int quantity) =>
            ForTotalDeposit(
                subtotalAfterDiscountCents,
                serviceChargeBps,
                riderPaidServiceChargeBps,
                totalDepositCents: (int)Math.Min(
                    (long)Math.Max(depositPerUnitCents, 0) * Math.Max(quantity, 0), int.MaxValue));

        /// <summary>
        /// A booking whose deposit has already been totalled across several different items, which
        /// is the shop rental case: each line carries its own per-unit deposit, so there is no single
        /// (deposit, quantity) pair to pass.
        /// </summary>
        public static RentalChargeAmounts ForTotalDeposit(
            int subtotalAfterDiscountCents,
            int serviceChargeBps,
            int riderPaidServiceChargeBps,
            int totalDepositCents)
        {
            // A discount can only take the subtotal to zero, never below; a negative base would
            // otherwise produce a negative fee, which is a credit we never intend to issue.
            var subtotal = Math.Max(subtotalAfterDiscountCents, 0);
            var deposit = Math.Max(totalDepositCents, 0);

            // Note what is NOT here: `deposit` appears in neither of the next two lines. That
            // absence is the invariant.
            var serviceCharge = (int)((long)subtotal * Math.Max(serviceChargeBps, 0) / 10_000L);
            var riderShare = (int)((long)serviceCharge * Math.Max(riderPaidServiceChargeBps, 0) / 10_000L);

            var rentalAmount = subtotal + riderShare;
            return new RentalChargeAmounts(
                ServiceChargeCents: serviceCharge,
                RiderServiceChargeCents: riderShare,
                RentalAmountCents: rentalAmount,
                DepositCents: deposit,
                GrossCents: rentalAmount + deposit);
        }
    }
}
