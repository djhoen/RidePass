namespace Services.Payments
{
    /// <summary>
    /// Tax on the buyer-paid platform service fee of a bike shop sale.
    ///
    /// Its own type for the same reason <see cref="ServiceChargeSplit"/> is: three separate paths
    /// create a shop_sale (the counter register, the online store, and a work-order bill-out), and a
    /// tax rule copied three times is a tax rule that will eventually differ in one of them. A
    /// customer being charged different tax at the counter than online is the kind of bug that
    /// surfaces in an audit rather than a bug report.
    ///
    /// The fee is taxed at the tenant's DEFAULT shop tax category rate. A per-product rate is
    /// meaningless for a charge that is not a product, and the default category is by definition the
    /// rate for anything not otherwise categorised. A tenant with no default category taxes the fee
    /// at nothing, exactly as their uncategorised products already sell untaxed.
    /// </summary>
    public static class ShopFeeTax
    {
        /// <summary>
        /// Tax owed on <paramref name="buyerFeeCents"/>, or 0 when the tenant has opted out, there
        /// is no fee, or there is no default tax category.
        /// </summary>
        /// <param name="buyerFeeCents">The customer's share of the service charge. Only what the
        /// CUSTOMER pays can be taxed to them; a fee the shop absorbs never reaches their total.</param>
        /// <param name="taxable">tenant.shop_tax_service_charge_taxable.</param>
        /// <param name="defaultTaxRateBps">The tenant's default shop tax category rate, or 0/null
        /// when they have none.</param>
        /// <param name="pricesIncludeTax">When true the fee is treated as tax-inclusive and the tax
        /// is extracted from it, matching how every line on the same sale is handled. Mixing the two
        /// conventions on one receipt would make the totals fail to reconcile.</param>
        public static int Compute(int buyerFeeCents, bool taxable, int? defaultTaxRateBps, bool pricesIncludeTax)
        {
            if (!taxable) return 0;
            if (buyerFeeCents <= 0) return 0;
            var rateBps = defaultTaxRateBps ?? 0;
            if (rateBps <= 0) return 0;

            if (pricesIncludeTax)
            {
                return buyerFeeCents
                    - (int)Math.Round(buyerFeeCents * 10000.0 / (10000.0 + rateBps), MidpointRounding.AwayFromZero);
            }
            return (int)Math.Round(buyerFeeCents * rateBps / 10000.0, MidpointRounding.AwayFromZero);
        }
    }
}
