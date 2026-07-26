using System;

namespace Services.Payments
{
    /// <summary>What RidePass is owed on a sale, and how much of it the customer funds.</summary>
    /// <param name="ServiceChargeCents">
    /// The full charge on the sale. What RidePass is owed <b>whoever funds it</b>, and therefore
    /// what the ledger books against the tenant's proceeds.
    /// </param>
    /// <param name="CustomerPaidCents">
    /// The share added to what the customer is actually charged. Zero means the track absorbs the
    /// whole thing: still owed, just out of their own margin rather than the customer's pocket.
    /// </param>
    public readonly record struct ServiceChargeAmounts(int ServiceChargeCents, int CustomerPaidCents);

    /// <summary>
    /// The one place the platform's service charge is split between the customer and the track.
    ///
    /// Extracted because rentals and shop sales were about to grow two copies of the same two
    /// lines, and two copies of a money calculation is how a quoted total stops matching what the
    /// card is charged. RentalCharge delegates here; so does the bike shop register.
    ///
    /// The integer math is deliberate and load-bearing: floor the tenant charge, then floor the
    /// customer's share of THAT. Both are truncating divisions on long intermediates so the figure
    /// a screen quotes is exactly the figure that gets charged. Do not "improve" this to rounding
    /// without changing every client-side estimate in the same commit.
    /// </summary>
    public static class ServiceChargeSplit
    {
        public static ServiceChargeAmounts Compute(int baseCents, int serviceChargeBps, int customerPaidBps)
        {
            // A discount can take a base to zero but never below; a negative base would produce a
            // negative charge, which is a credit we never intend to issue.
            var basis = Math.Max(baseCents, 0);
            var full = (int)((long)basis * Math.Max(serviceChargeBps, 0) / 10_000L);
            var customer = (int)((long)full * Math.Max(customerPaidBps, 0) / 10_000L);
            return new ServiceChargeAmounts(full, customer);
        }
    }
}
