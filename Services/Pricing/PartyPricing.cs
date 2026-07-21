using Services.Repositories.Data.PaymentData;

namespace Services.Pricing
{
    /// <summary>
    /// Party pricing for a ticket tier: "one price covers up to N riders", optionally with a
    /// fixed fee for each rider beyond that. Pure so the same rule drives online checkout, the
    /// counter, and any estimate shown on the buy page.
    ///
    /// Every rider still gets their own ticket row (capacity, roster, registration, and waivers
    /// all depend on that); only the PRICE varies by position within the purchase:
    ///   position 0                    -> the tier's base price
    ///   positions 1 .. Included-1     -> free, covered by that base price
    ///   positions Included and beyond -> PartyPriceCents, or the base price when it's null
    ///
    /// Defaults (Included = 1, PartyPriceCents = null) reproduce ordinary per-person pricing
    /// exactly, so tiers that never opt in are unaffected.
    /// </summary>
    public static class PartyPricing
    {
        /// <summary>Price for the rider at <paramref name="position"/> (0-based) within one
        /// purchase of this tier, before vouchers, pass benefits, and coupons.</summary>
        public static int UnitPriceCents(EventTicketTier tier, int position)
        {
            if (position <= 0) return tier.PriceCents;
            var included = Math.Max(1, tier.PartySizeIncluded);
            if (position < included) return 0;
            return tier.PartyPriceCents ?? tier.PriceCents;
        }

        /// <summary>Total for <paramref name="quantity"/> riders on this tier, before discounts.
        /// Used for buy-page estimates and counter totals.</summary>
        public static int TotalCents(EventTicketTier tier, int quantity)
        {
            var total = 0;
            for (var i = 0; i < quantity; i++) total += UnitPriceCents(tier, i);
            return total;
        }

        /// <summary>True when this tier advertises a party price worth showing to a buyer, i.e.
        /// the base covers more than one rider or extra riders are priced differently.</summary>
        public static bool IsPartyPriced(EventTicketTier tier) =>
            tier.PartySizeIncluded > 1
            || (tier.PartyPriceCents.HasValue && tier.PartyPriceCents.Value != tier.PriceCents);
    }
}
